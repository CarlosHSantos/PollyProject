using System.Threading.Tasks;
using PollyProject;

namespace PollyProject
{
    public interface IBasicClass
    {
        int GetSomeNumber();
        Task Func();
        Account CreateNewAccount(string name, int age);
    }
}