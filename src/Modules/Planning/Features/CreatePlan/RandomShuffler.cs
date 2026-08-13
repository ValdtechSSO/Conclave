using System.Security.Cryptography;
using Conclave.Planning;

namespace Conclave.Planning.Features.CreatePlan;

public sealed class RandomShuffler : IShuffler
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public IReadOnlyList<T> Shuffle<T>(IEnumerable<T> values)
    {
        var result = values.ToArray();
        for (var i = result.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }

    public string CreateAlias(ISet<string> existingAliases)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var alias = $"{Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]}{RandomNumberGenerator.GetInt32(10)}";
            if (existingAliases.Add(alias)) return alias;
        }
        throw new InvalidOperationException("Could not allocate a unique proposal alias.");
    }
}
