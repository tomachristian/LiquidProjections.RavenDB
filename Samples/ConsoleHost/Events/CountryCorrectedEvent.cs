using System;

namespace ConsoleHost.Events
{
    internal class CountryCorrectedEvent
    {
        public string DocumentNumber { get; set; }
        public Guid Country { get; set; }
    }
}