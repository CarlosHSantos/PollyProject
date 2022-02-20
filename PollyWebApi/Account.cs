namespace PollyWebApi
{
    public class Account
    {

        public string Name { get; set; }
        public int Age { get; set; }
        public Account(string name, int age)
        {
            Name = name;
            Age = age;
        }

        public static Account GetRandomAccount()
        {
            return new Account("Carlos", 31);
        }
    }
}