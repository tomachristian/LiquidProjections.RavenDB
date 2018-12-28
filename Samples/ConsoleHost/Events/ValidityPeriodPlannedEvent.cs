using System;

namespace ConsoleHost.Events
{
    internal class ValidityPeriodPlannedEvent
    {
        public int Sequence { get; set; }
        public string DocumentNumber { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
    }
}