using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace PollyProject
{
    public class BasicClass : IBasicClass
    {
        public int GetSomeNumber()
        {
            try
            {
                return 0;
            }
            catch (HttpRequestException)
            {
                return 999;
            }
        }

        public virtual async Task Func() { }

        public Account CreateNewAccount(string name, int age)
        {
            try
            {
                return new Account(name, age);
            }
            catch (Exception)
            {
                return null;
            }

        }
    }
}
