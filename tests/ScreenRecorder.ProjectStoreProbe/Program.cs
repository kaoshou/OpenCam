// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.Projects;

try
{
    await using var project = await new JsonProjectStore().OpenAsync(args[1]);
    Console.WriteLine("ACQUIRED");
    Console.Out.Flush();
    if (args[0] == "save-loop")
    {
        for (var i = 0; i < 10000; i++)
        {
            var current = project.Current;
            await project.SaveAsync(current with { Name = "revision-" + i, Revision = current.Revision + 1 }, current.Revision);
            Console.WriteLine("COMMITTED:" + project.Current.Revision);
            Console.Out.Flush();
        }
    }
    return 0;
}
catch (IOException)
{
    Console.WriteLine("BUSY_OR_INVALID");
    return 4;
}
