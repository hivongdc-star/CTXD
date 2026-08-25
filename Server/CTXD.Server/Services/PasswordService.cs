using System.Security.Cryptography;

namespace CTXD.Server.Services;

public static class PasswordService
{
    const int Iterations=210_000;
    public static string Hash(string password)
    {
        var salt=RandomNumberGenerator.GetBytes(16);
        var key=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }
    public static bool Verify(string password,string encoded)
    {
        try {
            var p=encoded.Split('$'); if(p.Length!=4 || p[0]!="pbkdf2-sha256") return false;
            var iter=int.Parse(p[1]); var salt=Convert.FromBase64String(p[2]); var expected=Convert.FromBase64String(p[3]);
            var actual=Rfc2898DeriveBytes.Pbkdf2(password,salt,iter,HashAlgorithmName.SHA256,expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual,expected);
        } catch { return false; }
    }
}
