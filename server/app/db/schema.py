import re


_IDENTIFIER_PATTERN = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")


def normalize_schema_name(schema_name: str) -> str:
    schema = schema_name.strip()
    if not schema:
        return ""
    if not _IDENTIFIER_PATTERN.fullmatch(schema):
        raise ValueError(f"Invalid database schema name: {schema_name!r}")
    return schema


def quote_identifier(identifier: str) -> str:
    return f'"{identifier}"'
