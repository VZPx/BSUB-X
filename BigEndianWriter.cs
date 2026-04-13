using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BSUB_X
{
    public class BigEndianWriter : BinaryWriter
    {
        public BigEndianWriter(Stream s) : base(s) { }

        public override void Write(int value)
        {
            byte[] data = BitConverter.GetBytes(value);
            Array.Reverse(data);
            base.Write(data);
        }

        public override void Write(short value)
        {
            byte[] data = BitConverter.GetBytes(value);
            Array.Reverse(data);
            base.Write(data);
        }
    }
}
