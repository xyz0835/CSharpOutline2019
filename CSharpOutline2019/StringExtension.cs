using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSharpOutline2019
{
    public static class StringExtension
    {
        public static int StartCharCount(this string str, char chr)
        {
            int count = 0;
            foreach (char item in str)
            {
                if (item.Equals(chr))
                    count++;
                else
                    break;
            }
            return count;
        }
    }
}
