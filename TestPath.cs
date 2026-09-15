using System;
using System.IO;
class Program {
    static void Main() {
        Console.WriteLine(AppDomain.CurrentDomain.BaseDirectory);
        Console.WriteLine(Environment.ProcessPath);
    }
}
