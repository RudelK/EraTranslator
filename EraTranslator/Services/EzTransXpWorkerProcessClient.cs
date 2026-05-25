using System.Diagnostics;
using System.Text;

namespace EraTranslator.Services;

internal interface IEzTransXpWorkerClient : IAsyncDisposable
{
    Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

internal interface IEzTransXpWorkerClientFactory
{
    IEzTransXpWorkerClient Create(EzTransXpInstallationInfo installationInfo);
}

internal sealed class EzTransXpWorkerClientFactory(string workerExecutablePath) : IEzTransXpWorkerClientFactory
{
    public IEzTransXpWorkerClient Create(EzTransXpInstallationInfo installationInfo)
    {
        return new EzTransXpWorkerProcessClient(workerExecutablePath, installationInfo);
    }
}

internal sealed class EzTransXpWorkerProcessClient : IEzTransXpWorkerClient
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private readonly StreamReader _stderr;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public EzTransXpWorkerProcessClient(string workerExecutablePath, EzTransXpInstallationInfo installationInfo)
    {
        if (!File.Exists(workerExecutablePath))
        {
            throw new FileNotFoundException("EzTransXP 워커 실행 파일을 찾지 못했습니다.", workerExecutablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = workerExecutablePath,
            Arguments = Convert.ToBase64String(Encoding.UTF8.GetBytes(installationInfo.InstallationPath)),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("EzTransXP 워커 프로세스를 시작하지 못했습니다.");
        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;
        _stderr = _process.StandardError;
    }

    public async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_process.HasExited)
            {
                throw CreateProcessExitException();
            }

            var request = new EzTransXpWorkerRequest(Guid.NewGuid().ToString("N"), texts);
            await _stdin.WriteLineAsync(EzTransXpWorkerProtocol.EncodeRequest(request));
            await _stdin.FlushAsync();

            var responseLine = await _stdout.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                throw CreateProcessExitException();
            }

            var response = EzTransXpWorkerProtocol.DecodeResponse(responseLine);
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                throw new InvalidOperationException(response.Error);
            }

            if (!string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("EzTransXP 워커 응답 식별자가 일치하지 않습니다.");
            }

            return response.Texts?.ToArray()
                ?? throw new InvalidOperationException("EzTransXP 워커 응답 본문이 비어 있습니다.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                _stdin.Close();
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            _stdin.Dispose();
            _stdout.Dispose();
            _stderr.Dispose();
            _process.Dispose();
            _gate.Dispose();
        }
    }

    private Exception CreateProcessExitException()
    {
        var stderr = string.Empty;
        try
        {
            stderr = _stderr.ReadToEnd();
        }
        catch
        {
        }

        return new InvalidOperationException(
            string.IsNullOrWhiteSpace(stderr)
                ? "EzTransXP 워커 프로세스가 비정상 종료되었습니다."
                : $"EzTransXP 워커 프로세스가 비정상 종료되었습니다: {stderr.Trim()}");
    }
}
