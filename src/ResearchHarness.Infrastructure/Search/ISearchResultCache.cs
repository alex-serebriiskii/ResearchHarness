using System.Diagnostics.CodeAnalysis;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Infrastructure.Search;

public interface ISearchResultCache
{
    bool TryGet(string query, [NotNullWhen(true)] out SearchResults? results);
    void Set(string query, SearchResults results);
}
