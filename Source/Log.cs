using System;

namespace Discord_IRC_Sharp
{
    public class Log
    {
        public static void Write(string message)
        {
            Console.WriteLine($"[{DateTime.Now.ToShortDateString()} {DateTime.Now.ToShortTimeString()}] {message}");
        }
    }
}