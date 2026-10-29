// Check for csharp-001 (marked answer: b).
// Runs the question's snippet unchanged, captures what it prints, and passes only if
// exactly one option matches the real output and that option is the marked answer.
// Exit code 0 = pass.

var options = new Dictionary<string, string>
{
    ["a"] = "True, True, True",
    ["b"] = "False, True, True",
    ["c"] = "False, False, True",
    ["d"] = "False, True, False",
};
const string marked = "b";

var captured = new StringWriter();
var stdout = Console.Out;
Console.SetOut(captured);

// --- snippet statements ---
IPrice a = new Price(10m, "EUR");
IPrice b = new Price(10m, "EUR");
Price c = new Price(10m, "EUR");

Console.WriteLine(a == b);
Console.WriteLine(a.Equals(b));
Console.WriteLine((Price)a == c);
// --- end snippet statements ---

Console.SetOut(stdout);
var printed = string.Join(", ", captured.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
var matching = options.Where(o => o.Value == printed).Select(o => o.Key).ToArray();

if (matching is not [marked])
{
    Console.Error.WriteLine($"FAIL csharp-001: printed \"{printed}\"; matching options [{string.Join(", ", matching)}]; marked {marked}");
    return 1;
}

Console.WriteLine($"PASS csharp-001: printed \"{printed}\", only option {marked} matches");
return 0;

// --- snippet types ---
interface IPrice
{
    decimal Amount { get; }
}

record Price(decimal Amount, string Currency) : IPrice;
