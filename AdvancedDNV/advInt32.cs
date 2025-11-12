using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedDNV
{
    public class advInt32
    {
        int value;

        internal advInt32()
        {
            value = 0;
        }

        internal advInt32 Add(int v) { value += v; return this; }
        internal advInt32 Set(int v) { value = v; return this; }
        internal int Get() { return value; }
    }
}
