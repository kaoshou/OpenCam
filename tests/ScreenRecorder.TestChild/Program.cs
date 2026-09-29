// SPDX-License-Identifier: AGPL-3.0-or-later
Console.WriteLine($"PID:{Environment.ProcessId}");
Console.Out.Flush();
switch (args.FirstOrDefault())
{
    case "success": return 0;
    case "exit-7": Console.Error.Write("encoder failed"); return 7;
    case "flood":
        await Task.WhenAll(Flood(Console.Out, 'o', "stdout-end"), Flood(Console.Error, 'e', "stderr-end"));
        return 0;
    case "delay": await Task.Delay(1800); return 0;
    case "wait": await Task.Delay(Timeout.Infinite); return 0;
    default: return 2;
}

static async Task Flood(TextWriter writer, char c, string ending)
{
    var chunk = new string(c, 4096);
    for (var i = 0; i < 512; i++) await writer.WriteAsync(chunk);
    await writer.WriteAsync(ending);
    await writer.FlushAsync();
}
