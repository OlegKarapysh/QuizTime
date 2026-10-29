namespace QuizBank;

/// <summary>
/// The machine can't run the command: Docker isn't running, git failed, PostgreSQL is the wrong version.
/// Commands report it and exit with 2, because the bank itself may be fine.
/// </summary>
public sealed class EnvironmentException(string message) : Exception(message);
