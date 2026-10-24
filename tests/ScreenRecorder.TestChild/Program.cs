// SPDX-License-Identifier: AGPL-3.0-or-later
if (args.FirstOrDefault() == "windows-native-helper")
{
    var mode = args.ElementAtOrDefault(1) ?? "valid";
    if (mode == "silent") { await Task.Delay(Timeout.Infinite); return 0; }
    Console.Error.WriteLine(mode == "bad-ready" ? "ERROR private-data-must-not-escape" : "READY bgra-matroska-clock-v1");
    Console.Error.Flush();
    if (Console.ReadLine() != "start") return 0;
    Console.Error.WriteLine("FRAME_CLOCK v1 0 1000 999 1");
    if (mode == "invalid") Console.Error.WriteLine("POINTER v1 1001 9999 11 1 1");
    else if (mode == "overflow") Console.Error.WriteLine("POINTER_ERROR queue-overflow");
    else if (mode == "oversized") Console.Error.WriteLine(new string('x', 100_000));
    else Console.Error.WriteLine("POINTER v1 1001 10 11 1 1");
    Console.Error.WriteLine("POINTER v1 1002 12 13 1 0");
    Console.Error.Flush();
    using var output = Console.OpenStandardOutput();
    output.Write(new byte[] { 0x42, 0x47, 0x52, 0x41 }); output.Flush();
    if (mode == "ignore-stop") { await Task.Delay(Timeout.Infinite); return 0; }
    if (mode == "early-exit") return 7;
    Console.ReadLine();
    Console.Error.WriteLine("STOP");
    return 0;
}
if (args.FirstOrDefault() == "fd-copy")
{
    // Exercise inherited shared native positions without a text/PID banner.
    using var input = Console.OpenStandardInput();
    using var output = Console.OpenStandardOutput();
    var buffer = new byte[4096];
    int count;
    while ((count = input.Read(buffer)) != 0)
    {
        output.Write(buffer, 0, count);
        Thread.Sleep(1);
    }
    output.Flush();
    return 0;
}
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
