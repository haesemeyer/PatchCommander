using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PatchCommander.Hardware
{
    static class HardwareSettings
    {
        public static class DAQ
        {
            public const string DeviceName = "Dev2";

            public const string Ch1Read = "ai0";

            public const string Ch2Read = "ai1";

            public const int Rate = 50000;

            public const string Ch1Mode = "port0/line0";

            public const string Ch2Mode = "port0/line1";
        }
    }
}
