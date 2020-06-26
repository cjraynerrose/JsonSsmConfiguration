using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace JsonSsmConfiguration
{
    public interface IRequest
    {
        public abstract Task Request(string[] input);
    }
}
