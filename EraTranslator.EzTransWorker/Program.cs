using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

var installationPath = ResolveInstallationPath(args);
if (string.IsNullOrWhiteSpace(installationPath))
{
    return;
}

var datPath = Path.Combine(installationPath, "Dat");
var enginePath = File.Exists(Path.Combine(installationPath, "J2KEngineH.dll"))
    ? Path.Combine(installationPath, "J2KEngineH.dll")
    : Path.Combine(installationPath, "J2KEngine.dll");

if (!Directory.Exists(datPath) || !File.Exists(enginePath))
{
    return;
}

using var translator = new EzTransNativeSession(enginePath, datPath);
using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
using var writer = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8)
{
    AutoFlush = true,
};

while (true)
{
    var payload = await reader.ReadLineAsync();
    if (string.IsNullOrWhiteSpace(payload))
    {
        break;
    }

    EzTransWorkerRequest? request = null;
    EzTransWorkerResponse response;
    try
    {
        request = EzTransWorkerProtocol.DecodeRequest(payload);
        var translated = request.Texts.Select(translator.Translate).ToArray();
        response = new EzTransWorkerResponse(request.RequestId, translated, null);
    }
    catch (Exception ex)
    {
        response = new EzTransWorkerResponse(request?.RequestId ?? string.Empty, null, ex.Message);
    }

    await writer.WriteLineAsync(EzTransWorkerProtocol.EncodeResponse(response));
}

static string ResolveInstallationPath(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return string.Empty;
    }

    try
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(arguments[0]));
    }
    catch
    {
        return string.Empty;
    }
}

internal static class EzTransWorkerProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static EzTransWorkerRequest DecodeRequest(string payload)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        return JsonSerializer.Deserialize<EzTransWorkerRequest>(json, JsonOptions)
            ?? throw new InvalidOperationException("EzTransXP 요청을 읽지 못했습니다.");
    }

    public static string EncodeResponse(EzTransWorkerResponse response)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, JsonOptions)));
    }
}

internal sealed record EzTransWorkerRequest(string RequestId, IReadOnlyList<string> Texts);

internal sealed record EzTransWorkerResponse(string RequestId, IReadOnlyList<string>? Texts, string? Error);

internal sealed class EzTransNativeSession : IDisposable
{
    private readonly nint _libraryHandle;
    private readonly J2KInitializeExDelegate _initializeEx;
    private readonly J2KTranslateDelegate _translate;
    private readonly J2KStopTranslationDelegate? _stopTranslation;
    private readonly J2KTerminateDelegate? _terminate;
    private bool _disposed;

    public EzTransNativeSession(string libraryPath, string datPath)
    {
        AddDllDirectory(Path.GetDirectoryName(libraryPath)!);
        _libraryHandle = NativeLibrary.Load(libraryPath);
        _initializeEx = LoadFunction<J2KInitializeExDelegate>("J2K_InitializeEx");
        _translate = LoadFunction<J2KTranslateDelegate>("J2K_TranslateMMNTW");
        _stopTranslation = TryLoadFunction<J2KStopTranslationDelegate>("J2K_StopTranslation");
        _terminate = TryLoadFunction<J2KTerminateDelegate>("J2K_Terminate");

        if (!_initializeEx("CSUSER123455", datPath))
        {
            throw new InvalidOperationException("J2K_InitializeEx 호출에 실패했습니다.");
        }
    }

    public string Translate(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var preprocessed = PreprocessString(text);
        var pointer = _translate(0, preprocessed);
        if (pointer == nint.Zero)
        {
            return preprocessed;
        }

        return Marshal.PtrToStringUni(pointer) ?? preprocessed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _stopTranslation?.Invoke();
            _terminate?.Invoke();
        }
        finally
        {
            NativeLibrary.Free(_libraryHandle);
        }
    }

    private TDelegate LoadFunction<TDelegate>(string functionName)
        where TDelegate : Delegate
    {
        var address = NativeLibrary.GetExport(_libraryHandle, functionName);
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
    }

    private TDelegate? TryLoadFunction<TDelegate>(string functionName)
        where TDelegate : Delegate
    {
        return NativeLibrary.TryGetExport(_libraryHandle, functionName, out var address)
            ? Marshal.GetDelegateForFunctionPointer<TDelegate>(address)
            : null;
    }

    private static string PreprocessString(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '『':
                case '』':
                    builder.Append('"');
                    break;
                case '｢':
                case '「':
                case '｣':
                case '」':
                    builder.Append('\'');
                    break;
                case '≪':
                    builder.Append('<');
                    break;
                case '≫':
                    builder.Append('>');
                    break;
                case '（':
                    builder.Append('(');
                    break;
                case '）':
                    builder.Append(')');
                    break;
                case '…':
                    builder.Append("...");
                    break;
                case '：':
                    builder.Append('￤');
                    break;
                case '・':
                    builder.Append('-');
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string? lpPathName);

    private static void AddDllDirectory(string directoryPath)
    {
        SetDllDirectory(directoryPath);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate bool J2KInitializeExDelegate(string username, string path);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate nint J2KTranslateDelegate(int data0, [MarshalAs(UnmanagedType.LPWStr)] string toTranslate);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void J2KStopTranslationDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void J2KTerminateDelegate();
}
