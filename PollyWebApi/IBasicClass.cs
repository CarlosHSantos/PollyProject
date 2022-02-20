using System.Threading.Tasks;

namespace PollyWebApi
{
    public interface IBasicClass
    {
        int GetSomeNumber();
        Task Func();
        Account CreateNewAccount(string name, int age);
    }
}