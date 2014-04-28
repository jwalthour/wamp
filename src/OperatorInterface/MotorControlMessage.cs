using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TankControlGui
{
    public class MotorControlMessage
    {
        public Boolean[] Fwd;
        public short[] Power;
        public int EffectiveTimeMs;
    }
}
