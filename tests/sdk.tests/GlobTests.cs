using Rhino.AI;

namespace sdk.tests;

public class GlobTests
{
    
    [TestCase("C:/Users/Desktop")]
    [TestCase("C:/Users/Desktop/Myfile.txt")]
    [TestCase("~/Users/Desktop")]
    [TestCase("~\\Users\\Desktop")]
    public void MatchesWithoutAsterisk(string path)
    {
        GlobPattern glob = new (path, '/', true);
        Assert.That(glob.IsMatch(path));
        
        glob = new (path, '\\', true);
        Assert.That(glob.IsMatch(path));
    }
    
    [TestCase("C:/Users/Desktop/Myfile.txt", "C:/**/Desktop/Myfile.txt")]
    [TestCase("C:/Users/Desktop/Myfile.txt", "C:/**/Myfile.txt")]
    [TestCase("C:/Myfile.txt", "C:/*.txt")]
    public void MatchesWithAsterisk(string path, string pattern)
    {
        GlobPattern glob = new (pattern, '/', true);
        Assert.That(glob.IsMatch(path));
        
        glob = new (pattern, '\\', true);
        Assert.That(glob.IsMatch(path));
    }
    
    [TestCase("C:/Users/Desktop", "C:/Users/Elsewhere")]
    [TestCase("C:/Users/Desktop/Myfile.txt", "C:/Users/Elsewhere/Myfile.txt")]
    [TestCase("~/Users/Desktop", "~/Users/Elsewhere")]
    [TestCase("~\\Users\\Desktop", "~\\Users\\Elsewhere")]
    public void DoesNotMatchWithoutAsterisk(string path, string pattern)
    {
        GlobPattern glob = new (pattern, '/', true);
        Assert.That(glob.IsMatch(path), Is.False);
        
        glob = new (pattern, '\\', true);
        Assert.That(glob.IsMatch(path), Is.False);
    }
    
    [TestCase("C:/Place/Myfile.txt", "C:/**/Desktop/Myfile.txt")]
    [TestCase("C:/Myfile.rtf", "C:/*.txt")]
    public void DoesNotMatchWithAsterisk(string path, string pattern)
    {
        GlobPattern glob = new (pattern, '/', true);
        Assert.That(glob.IsMatch(path), Is.False);
        
        glob = new (pattern, '\\', true);
        Assert.That(glob.IsMatch(path), Is.False);
    }

}
