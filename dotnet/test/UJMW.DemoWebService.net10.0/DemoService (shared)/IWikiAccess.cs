using DistributedDataFlow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Demo {

  public class WikiArtickleInfo {
    public Guid ArticleId { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
  }

  /// <summary>
  /// Provides read and write access to in-memory wiki articles for MCP demonstration scenarios.
  /// </summary>
  public interface IWikiAccess {

    /// <summary>
    /// Searches all available wiki articles for the specified keyword.
    /// </summary>
    /// <param name="keyword">The keyword to search for in article content.</param>
    /// <returns>The matching wiki article metadata entries.</returns>
    WikiArtickleInfo[] SearchArticleByKeyword(string keyword);

    /// <summary>
    /// Reads the content of an existing wiki article.
    /// </summary>
    /// <param name="articleId">The unique identifier of the wiki article.</param>
    /// <returns>The article content, or an empty string if the article does not exist.</returns>
    string ReadArticleContent(Guid articleId);

    /// <summary>
    /// Creates a new wiki article with the specified content.
    /// </summary>
    /// <param name="articleId">The unique identifier of the new wiki article.</param>
    /// <param name="content">The initial article content.</param>
    /// <returns>The stored article content.</returns>
    string CreateNewArtickleContent(Guid articleId, string content);

    /// <summary>
    /// Appends text to an existing wiki article.
    /// </summary>
    /// <param name="articleId">The unique identifier of the wiki article.</param>
    /// <param name="content">The content to append.</param>
    /// <returns>The complete article content after appending.</returns>
    string AppendArticleContent(Guid articleId, string content);

    /// <summary>
    /// Replaces the complete content of an existing wiki article.
    /// </summary>
    /// <param name="articleId">The unique identifier of the wiki article.</param>
    /// <param name="content">The replacement article content.</param>
    /// <returns>The stored replacement content.</returns>
    string OverwriteArticleContent(Guid articleId, string content);

  }


  public class WikiAccessService : IWikiAccess {
    //vollständige demo-implementierung , die nur in-memory arbeitet und keine echte Datenbank verwendet

    private readonly Dictionary<Guid, string> _articles = new Dictionary<Guid, string>();
    private readonly object _syncRoot = new object();

    public WikiAccessService() { }

    public WikiArtickleInfo[] SearchArticleByKeyword(string keyword) {
      if (string.IsNullOrWhiteSpace(keyword)) {
        return Array.Empty<WikiArtickleInfo>();
      }

      lock (_syncRoot) {
        var result = new List<WikiArtickleInfo>();
        foreach (var pair in _articles) {
          var content = pair.Value ?? string.Empty;
          if (content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) {
            result.Add(new WikiArtickleInfo {
              ArticleId = pair.Key,
              Title = $"Article {pair.Key}",
              Description = $"Contains '{keyword}'"
            });
          }
        }

        return result.ToArray();
      }
    }

    public string ReadArticleContent(Guid articleId) {
      lock (_syncRoot) {
        return _articles.TryGetValue(articleId, out var content)
          ? content
          : string.Empty;
      }
    }

    public string CreateNewArtickleContent(Guid articleId, string content) {
      lock (_syncRoot) {
        if (_articles.ContainsKey(articleId)) {
          throw new InvalidOperationException($"Article '{articleId}' already exists.");
        }

        _articles[articleId] = content ?? string.Empty;
        return _articles[articleId];
      }
    }

    public string AppendArticleContent(Guid articleId, string content) {
      lock (_syncRoot) {
        if (!_articles.TryGetValue(articleId, out var existingContent)) {
          throw new KeyNotFoundException($"Article '{articleId}' was not found.");
        }

        _articles[articleId] = (existingContent ?? string.Empty) + (content ?? string.Empty);
        return _articles[articleId];
      }
    }

    public string OverwriteArticleContent(Guid articleId, string content) {
      lock (_syncRoot) {
        if (!_articles.ContainsKey(articleId)) {
          throw new KeyNotFoundException($"Article '{articleId}' was not found.");
        }

        _articles[articleId] = content ?? string.Empty;
        return _articles[articleId];
      }
    }

    public WikiArtickleInfo[] ImportMarkdownFilesFromFolder(string folderPath) {
      if (string.IsNullOrWhiteSpace(folderPath)) {
        throw new ArgumentException("The folder path must not be empty.", nameof(folderPath));
      }

      string fullFolderPath = Path.GetFullPath(folderPath);
      if (!Directory.Exists(fullFolderPath)) {
        throw new DirectoryNotFoundException("The folder '" + fullFolderPath + "' was not found.");
      }

      string[] markdownFiles = Directory.GetFiles(fullFolderPath, "*.md", SearchOption.AllDirectories);
      List<WikiArtickleInfo> importedArticles = new List<WikiArtickleInfo>();

      lock (_syncRoot) {
        foreach (string markdownFile in markdownFiles) {
          string articleContent = File.ReadAllText(markdownFile, Encoding.UTF8);
          Guid articleId = this.CreateArticleIdFromFilePath(markdownFile);
          _articles[articleId] = articleContent;

          importedArticles.Add(new WikiArtickleInfo {
            ArticleId = articleId,
            Title = Path.GetFileNameWithoutExtension(markdownFile),
            Description = this.CreateArticleDescription(fullFolderPath, markdownFile)
          });
        }
      }

      return importedArticles.ToArray();
    }

    private Guid CreateArticleIdFromFilePath(string filePath) {
      string normalizedPath = Path.GetFullPath(filePath).ToUpperInvariant();
      byte[] inputBytes = Encoding.UTF8.GetBytes(normalizedPath);
      using (SHA256 sha256 = SHA256.Create()) {
        byte[] hashBytes = sha256.ComputeHash(inputBytes);
        byte[] guidBytes = new byte[16];
        Array.Copy(hashBytes, guidBytes, guidBytes.Length);
        return new Guid(guidBytes);
      }
    }

    private string CreateArticleDescription(string rootFolderPath, string filePath) {
      string relativePath = Path.GetRelativePath(rootFolderPath, filePath);
      return "Imported from Markdown file '" + relativePath + "'.";
    }

  }

}
