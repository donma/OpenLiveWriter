﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

using OpenLiveWriter.CoreServices;
using OpenLiveWriter.Extensibility.BlogClient;

namespace OpenLiveWriter.BlogClient.Clients.StaticSite
{
    public class StaticSitePost
    {
        /// <summary>
        /// The extension of published posts to the site project, including dot.
        /// </summary>
        public static string PUBLISH_FILE_EXTENSION = ".html";

        private static Regex POST_PARSE_REGEX = new Regex("^---\r?\n((?:.*\r?\n)*?)---\r?\n\r?\n((?:.*\r?\n)*)");
        private static Regex FILENAME_SLUG_REGEX = new Regex(@"^(?:.*?)\d\d\d\d-\d\d-\d\d-(.*?)\" + PUBLISH_FILE_EXTENSION + "$");

        private StaticSiteConfig SiteConfig;
        public BlogPost BlogPost { get; private set; }

        public StaticSitePost(StaticSiteConfig config)
        {
            SiteConfig = config;
            BlogPost = null;
        }

        public StaticSitePost(StaticSiteConfig config, BlogPost blogPost)
        {
            SiteConfig = config;
            BlogPost = blogPost;
        }

        public StaticSitePostFrontMatter FrontMatter
        {
            get => StaticSitePostFrontMatter.GetFromBlogPost(BlogPost);
        }

        /// <summary>
        /// Converts the post to a string, ready to be written to disk
        /// </summary>
        /// <returns>String representation of the post, including front-matter, lines separated by LF</returns>
        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendLine("---");
            builder.Append(FrontMatter.ToString());
            builder.AppendLine("---");
            builder.AppendLine();
            builder.Append(BlogPost.Contents);
            return builder.ToString().Replace("\r\n", "\n");
        }

        /// <summary>
        /// Unique ID of the BlogPost
        /// </summary>
        public string Id
        {
            get => BlogPost.Id;
            set => BlogPost.Id = value;
        }

        /// <summary>
        /// Get the slug for the post, or generate it if it currently does not have one
        /// </summary>
        public string Slug
        {
            get => _safeSlug;
            set => BlogPost.Slug = _safeSlug = value;
        }

        /// <summary>
        /// Confirmed safe slug; does not conflict with any existing post on disk or points to this post on disk.
        /// </summary>
        private string _safeSlug;

        /// <summary>
        /// Get the current on-disk slug from the on-disk post with this ID
        /// </summary>
        public string DiskSlug
        {
            get => FilePathById == null ? null : GetSlugFromPublishFileName(FilePathById);
        }

        public DateTime DatePublished
        {
            get => BlogPost.HasDatePublishedOverride ? BlogPost.DatePublishedOverride : BlogPost.DatePublished;
            set => BlogPost.DatePublished = BlogPost.DatePublishedOverride = value;
        }

        /// <summary>
        /// Get the on-disk file path for the published post, based on slug
        /// </summary>
        public string FilePathBySlug
        {
            get => GetFilePathForProvidedSlug(Slug);
        }

        private string _filePathById;
        /// <summary>
        /// Get the on-disk file path for the published post, based on ID
        /// </summary>
        public string FilePathById
        {
            get
            {
                if (_filePathById != null) return _filePathById;
                var foundFile = Directory.GetFiles(Path.Combine(SiteConfig.LocalSitePath, SiteConfig.PostsPath), "*.html")
                .Where(postFile =>
                {
                    try
                    {
                        var post = StaticSitePost.LoadFromFile(Path.Combine(SiteConfig.LocalSitePath, SiteConfig.PostsPath, postFile), SiteConfig);
                        if (post.Id == Id) return true;
                    }
                    catch { }

                    return false;
                }).DefaultIfEmpty(null).FirstOrDefault();
                return _filePathById = (foundFile == null ? null : Path.Combine(SiteConfig.LocalSitePath, SiteConfig.PostsPath, foundFile));
            }

            protected set => _filePathById = value;
        }

        /// <summary>
        /// Get the site path for the published post
        /// eg. /2019/01/slug.html
        /// TODO consider removing this feature and associated config setting as we don't actually need this information 
        /// </summary>
        public string SitePath
        {
            get
            {
                if (BlogPost.IsPage) throw new NotImplementedException(); // TODO 

                return SiteConfig.PostUrlFormat
                .Replace("%y", BlogPost.DatePublished.ToString("yyyy"))
                .Replace("%m", BlogPost.DatePublished.ToString("MM"))
                .Replace("%d", BlogPost.DatePublished.ToString("dd"))
                .Replace("%f", $"{Slug}{PUBLISH_FILE_EXTENSION}");
            }
        }
            
        /// <summary>
        /// Generate a safe slug if the post doesn't already have one. Returns the current or new Slug.
        /// </summary>
        /// <returns>The current or new Slug.</returns>
        public string EnsureSafeSlug()
        {
            if (_safeSlug == null || _safeSlug == string.Empty) Slug = FindNewSlug(BlogPost.Slug, safe: true);
            return Slug;
        }

        /// <summary>
        /// Generate a new Id and save it to the BlogPost if requried. Returns the current or new Id.
        /// </summary>
        /// <returns>The current or new Id</returns>
        public string EnsureId()
        {
            if(Id == null || Id == string.Empty) Id = Guid.NewGuid().ToString();
            return Id;
        }

        /// <summary>
        /// Set post published DateTime to current DateTime if one isn't already set, or current one is default.
        /// </summary>
        /// <returns>The current or new DatePublished.</returns>
        public DateTime EnsureDatePublished()
        {
            if (DatePublished == null || DatePublished == new DateTime(1, 1, 1)) DatePublished = DateTime.UtcNow;
            return DatePublished;
        }

        /// <summary>
        /// Generate a slug for this post based on it's title or a preferred slug
        /// </summary>
        /// <param name="preferredSlug">The text to base the preferred slug off of. default: post title</param>
        /// <param name="safe">Safe mode; if true the returned slug will not conflict with any existing file</param>
        /// <returns>An on-disk slug for this post</returns>
        public string FindNewSlug(string preferredSlug, bool safe)
        {
            // Try the filename without a duplicate identifier, then duplicate identifiers up until 999 before throwing an exception
            for(int i = 0; i < 1000; i++)
            {
                // "Hello World!" -> "hello-world"
                string slug = StaticSiteClient.WEB_UNSAFE_CHARS
                    .Replace((preferredSlug == string.Empty ? BlogPost.Title : preferredSlug).ToLower(), "")
                    .Replace(" ", "-");
                if (!safe) return slug; // If unsafe mode, return the generated slug immediately.

                if (i > 0) slug += $"-{i}";
                if (!File.Exists(GetFilePathForProvidedSlug(slug))) return slug;
            }

            // Couldn't find an available filename, use the post's ID.
            return StaticSiteClient.WEB_UNSAFE_CHARS.Replace(EnsureId(), "").Replace(" ", "-");
        }

        /// <summary>
        /// Get the Post filenpame for the provided slug
        /// </summary>
        /// <param name="slug"></param>
        private string GetFileNameForProvidedSlug(string slug)
        {
            if(BlogPost.IsPage && SiteConfig.PagesEnabled)
            {
                return $"{BlogPost.DatePublished.ToString("yyyy-MM-dd")}-{slug}{PUBLISH_FILE_EXTENSION}";
            }
            else
            {
                return $"{BlogPost.DatePublished.ToString("yyyy-MM-dd")}-{slug}{PUBLISH_FILE_EXTENSION}";
            }
        }

        private string GetFilePathForProvidedSlug(string slug)
        {
            if (BlogPost.IsPage && SiteConfig.PagesEnabled)
            {
                return Path.Combine(
                    SiteConfig.LocalSitePath,
                    SiteConfig.PagesPath,
                    GetFileNameForProvidedSlug(slug));
            }
            else
            {
                return Path.Combine(
                    SiteConfig.LocalSitePath,
                    SiteConfig.PostsPath,
                    GetFileNameForProvidedSlug(slug));
            }
        }

        private string GetSlugFromPublishFileName(string publishFileName) => FILENAME_SLUG_REGEX.Match(publishFileName).Groups[1].Value;

        /// <summary>
        /// Save the post to the correct directory
        /// </summary>
        public void SaveToFile(string postFilePath)
        {
            // Save the post to disk
            File.WriteAllText(postFilePath, ToString());
        }

        /// <summary>
        /// Load published post from a specified file path
        /// </summary>
        /// <param name="postFilePath">Path to published post file</param>
        public void LoadFromFile(string postFilePath)
        {
            // Attempt to load file contents
            var fileContents = File.ReadAllText(postFilePath);

            // Parse out everything between triple-hyphens into front matter parser
            var frontMatterMatchResult = POST_PARSE_REGEX.Match(fileContents);
            if (!frontMatterMatchResult.Success || frontMatterMatchResult.Groups.Count < 3) throw new BlogClientException("Post load error", "Could not read post front matter"); // TODO Use strings resources
            var frontMatterYaml = frontMatterMatchResult.Groups[1].Value;
            var postContent = frontMatterMatchResult.Groups[2].Value;

            // Create a new BlogPost
            BlogPost = new BlogPost();
            // Parse front matter and save in
            StaticSitePostFrontMatter.GetFromYaml(frontMatterYaml).SaveToBlogPost(BlogPost);

            // Throw error if post does not have an ID
            if (Id == null || Id == string.Empty) throw new BlogClientException("Post load error", "Post does not have an ID");

            // FilePathById will be the path we loaded this post from
            FilePathById = postFilePath;

            // Load the content into blogpost
            BlogPost.Contents = postContent;

            // Set slug to match file name
            Slug = GetSlugFromPublishFileName(Path.GetFileName(postFilePath));
        }

        /// <summary>
        /// Load published post from a specified file path
        /// </summary>
        /// <param name="postFilePath">Path to published post file</param>
        /// <param name="config">StaticSiteConfig to instantiate post with</param>
        /// <returns>A loaded StaticSitePost</returns>
        public static StaticSitePost LoadFromFile(string postFilePath, StaticSiteConfig config)
        {
            var post = new StaticSitePost(config);
            post.LoadFromFile(postFilePath);
            return post;
        }

        /// <summary>
        /// Get all valid posts in PostsPath
        /// </summary>
        /// <returns>An IEnumerable of StaticSitePost</returns>
        public static IEnumerable<StaticSitePost> GetAllPosts(StaticSiteConfig config) =>
            Directory.GetFiles(Path.Combine(config.LocalSitePath, config.PostsPath), "*.html")
            .Select(postFile =>
            {
                try
                {
                    return StaticSitePost.LoadFromFile(Path.Combine(config.LocalSitePath, config.PostsPath, postFile), config);
                }
                catch { return null; }
            })
            .Where(p => p != null);

        public static StaticSitePost GetPostById(StaticSiteConfig config, string id)
            => GetAllPosts(config).Where(post => post.Id == id).DefaultIfEmpty(null).FirstOrDefault();
    }
}
