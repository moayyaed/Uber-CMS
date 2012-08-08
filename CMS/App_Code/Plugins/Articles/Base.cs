﻿using System;
using System.Collections.Generic;
using System.Web;
using System.Text;
using UberLib.Connector;

namespace UberCMS.Plugins
{
    public static class Articles
    {
        public const int TITLE_MAX = 45;
        public const int TITLE_MIN = 1;
        public const int BODY_MIN = 1;
        public const int BODY_MAX = 8000; // Consider making this a setting
        public const int RELATIVE_URL_CHUNK_MIN = 1;
        public const int RELATIVE_URL_CHUNK_MAX = 32;
        public const int RELATIVE_URL_MAXCHUNKS = 8;
        public const int TAGS_TITLE_MIN = 1;
        public const int TAGS_TITLE_MAX = 24;
        public const int TAGS_MAX = 20;

        public const string SETTINGS_KEY = "articles";
        public const string SETTINGS_KEY_HANDLES_404 = "handles_404";

        public static string enable(string pluginid, Connector conn)
        {
            string basePath = Misc.Plugins.getPluginBasePath(pluginid, conn);
            string error = null;
            // Reserve URLS
            if ((error = Misc.Plugins.reserveURLs(pluginid, null, new string[] { "article", "articles" }, conn)) != null)
                return error;
            // Install content
            if((error = Misc.Plugins.contentInstall(basePath + "\\Content")) != null)
                return error;
            // Install templates
            if ((error = Misc.Plugins.templatesInstall(basePath + "\\Templates", conn)) != null)
                return error;
            // Install SQL
            if ((error = Misc.Plugins.executeSQL(basePath + "\\SQL\\Install.sql", conn)) != null)
                return error;
            // Install settings
            Core.settings.updateSetting(conn, pluginid, SETTINGS_KEY, SETTINGS_KEY_HANDLES_404, "1", "Any 404/unhandled pages will be handled by article create - like Wikis.", false);

            return null;
        }
        public static string disable(string pluginid, Connector conn)
        {
            string basePath = Misc.Plugins.getPluginBasePath(pluginid, conn);
            string error = null;
            // Unreserve base URLs
            if ((error = Misc.Plugins.unreserveURLs(pluginid, conn)) != null)
                return error;
            // Uninstall content
            if ((error = Misc.Plugins.contentUninstall(basePath + "\\Content")) != null)
                return error;
            // Uninstall templates
            if ((error = Misc.Plugins.templatesUninstall("articles", conn)) != null)
                return error;

            return null;
        }
        public static string uninstall(string pluginid, Connector conn)
        {
            string basePath = Misc.Plugins.getPluginBasePath(pluginid, conn);
            string error = null;
            // Unreserve all URLs
            if((error = Misc.Plugins.unreserveURLs(pluginid, conn)) != null)
                return error;
            // Uninstall SQL
            if ((error = Misc.Plugins.executeSQL(basePath + "\\SQL\\Uninstall.sql", conn)) != null)
                return error;
            // Remove settings
            Core.settings.removeCategory(conn, SETTINGS_KEY);

            return null;
        }
        public static void handleRequest(string pluginid, Connector conn, ref Misc.PageElements pageElements, HttpRequest request, HttpResponse response, ref string baseTemplateParent)
        {
            switch (request.QueryString["page"])
            {
                case "article":
                    pageArticle(pluginid, conn, ref pageElements, request, response, ref baseTemplateParent);
                    break;
                case "articles":
                    pageArticles(pluginid, conn, ref pageElements, request, response, ref baseTemplateParent);
                    break;
                default:
                    pageArticle_Editor(pluginid, conn, ref pageElements, request, response, ref baseTemplateParent);
                    break;
            }
        }
        public static void pageArticles(string pluginid, Connector conn, ref Misc.PageElements pageElements, HttpRequest request, HttpResponse response, ref string baseTemplateParent)
        {
            switch (request.QueryString["1"])
            {
                default:
                    pageArticles_Browse(pluginid, conn, ref pageElements, request, response, ref baseTemplateParent);
                    break;
            }
        }
        public static void pageArticles_Browse(string pluginid, Connector conn, ref Misc.PageElements pageElements, HttpRequest request, HttpResponse response, ref string baseTemplateParent)
        {
        }
        public static void pageArticle(string pluginid, Connector conn, ref Misc.PageElements pageElements, HttpRequest request, HttpResponse response, ref string baseTemplateParent)
        {
            switch (request.QueryString["1"])
            {
                default:
                    pageArticle_View(pluginid, conn, ref pageElements, request, response, ref baseTemplateParent);
                    break;
                case "create":
                case "editor":
                    pageArticle_Editor(pluginid, conn, ref pageElements, request, response, ref baseTemplateParent);
                    break;
                case "delete":
                    pageArticle_Delete(pluginid, conn, ref pageElements, request, response, ref baseTemplateParent);
                    break;
            }
        }
        public static void pageArticle_View(string pluginid, Connector conn, ref Misc.PageElements pageElements, HttpRequest request, HttpResponse response, ref string baseTemplateParent)
        {
            // Retrieve the article ID
            string articleid;
            if (request.QueryString["page"] == "article")
                articleid = request.QueryString["1"];
            else
            {
                // Build the relative URL
                StringBuilder relativeUrl = new StringBuilder();
                relativeUrl.Append(request.QueryString["page"]).Append("/"); // The querystring "pg" should never be null, however no null exception will occur with stringbuilder anyhow
                string chunk;
                for (int i = 1; i <= RELATIVE_URL_MAXCHUNKS; i++)
                {
                    chunk = request.QueryString[i.ToString()];
                    if (chunk != null)
                    {
                        if (chunk.Length > RELATIVE_URL_CHUNK_MAX)
                            return; // Invalid request - hence 404...
                        else
                            relativeUrl.Append(chunk).Append("/");
                    }
                    else
                        break;
                }
                // Check if we've grabbed anything
                if (relativeUrl.Length == 0)
                    return; // No URL captured - 404...
                else
                    relativeUrl.Remove(relativeUrl.Length - 1, 1); // Remove tailing slash
                // Grab the article ID from the database
                articleid = (conn.Query_Scalar("SELECT articleid_current FROM articles_thread WHERE relative_url='" + Utils.Escape(relativeUrl.ToString()) + "'") ?? string.Empty).ToString();
            }
            // Check we have an articleid that is not null and greater than zero, else 404
            if (articleid.Length == 0) return;
            // Load the article's data
            Result articleRaw = conn.Query_Read("SELECT a.*, at.relative_url, at.articleid_current FROM articles AS a, articles_thread AS at WHERE a.articleid='" + Utils.Escape(articleid) + "' AND at.threadid=a.threadid");
            if (articleRaw.Rows.Count != 1)
                return; // 404 - no data found - the article is corrupt (thread and article not linked) or the article does not exist
            ResultRow article = articleRaw[0];
            // Load the users permissions
            bool permCreate;
            bool permDelete;
            bool permPublish;
            bool owner;
            if (HttpContext.Current.User.Identity.IsAuthenticated)
            {
                Result permsRaw = conn.Query_Read("SELECT access_media_create, access_media_delete, access_media_publish FROM bsa_users AS u LEFT OUTER JOIN bsa_user_groups AS g ON g.groupid=u.groupid WHERE u.userid='" + Utils.Escape(HttpContext.Current.User.Identity.Name) + "'");
                if(permsRaw.Rows.Count != 1) return; // Something has gone wrong
                ResultRow perms = permsRaw[0];
                permCreate = perms["access_media_create"].Equals("1");
                permDelete = perms["access_media_delete"].Equals("1");
                permPublish = perms["access_media_publish"].Equals("1");
                owner = article["userid"] == HttpContext.Current.User.Identity.Name;
            }
            else
            {
                permCreate = false;
                permDelete = false;
                permPublish = false;
                owner = false;
            }
            // Create stringbuilder for assembling the article
            StringBuilder content = new StringBuilder();
            // Check the article is published *or* the user is admin/owner of the article
            if (!article["published"].Equals("1"))
            {
                // Check the user has permission
                if (!HttpContext.Current.User.Identity.IsAuthenticated || (!owner && !permPublish)) return;
                // Add a notice about the article being unpublished
                content.Append(Core.templates["articles"]["unpublished_header"]);
            }
            // Append the main body of the article
            content.Append(Core.templates["articles"]["article"]);

            // Render the article's body
            content.Replace("%BODY%", article["body"]);

            // Add pane
            // -- Build buttons the user can utilize
            StringBuilder buttons = new StringBuilder();
            // -- Finalize
            content
                .Replace("%DATE%", article["datetime"])
                .Replace("%DATE_SIMPLE%", article["datetime"].Length > 0 ? Misc.Plugins.getTimeString(DateTime.Parse(article["datetime"])) : "unknown")
                .Replace("%BUTTONS%", buttons.ToString())
                ;

            // Add comments

            pageElements["TITLE"] = HttpUtility.HtmlEncode(article["title"]);
            pageElements["CONTENT"] = content.ToString();
            Misc.Plugins.addHeaderCSS(pageElements["URL"] + "/Content/CSS/Article.css", ref pageElements);
        }
        public static void pageArticle_Editor(string pluginid, Connector conn, ref Misc.PageElements pageElements, HttpRequest request, HttpResponse response, ref string baseTemplateParent)
        {
            // Check the user is logged-in, else redirect to the login page
            if (!HttpContext.Current.User.Identity.IsAuthenticated)
                response.Redirect(pageElements["URL"] + "/login", true);

            string error = null;
            Result preData = null;
            ResultRow preDataRow = null;
            // Check if we're modifying an existing article, if so we'll load the data
            string articleid = request.QueryString["articleid"];
            if (articleid != null && Misc.Plugins.isNumeric(articleid))
            {
                // Attempt to load the pre-existing article's data
                preData = conn.Query_Read("SELECT a.*, at.relative_url, GROUP_CONCAT(at2.keyword SEPARATOR ',') AS tags FROM articles AS a LEFT OUTER JOIN articles_tags AS at2 ON (EXISTS (SELECT tagid FROM articles_tags_article WHERE tagid=at2.tagid AND articleid='" + Utils.Escape(articleid) + "')) LEFT OUTER JOIN articles_thread AS at ON at.threadid=a.threadid WHERE articleid='" + Utils.Escape(articleid) + "'");
                if (preData.Rows.Count != 1) preData = null;
                else
                    preDataRow = preData[0];
            }
            // Check for postback
            string title = request.Form["title"];
            string body = request.Form["body"];
            string relativeUrl = request.Form["relative_url"] ?? request.QueryString["relative_url"];
            string tags = request.Form["tags"];
            bool allowHTML = request.Form["allow_html"] != null;
            bool allowComments = request.Form["allow_comments"] != null;
            bool showPane = request.Form["show_pane"] != null;
            if (title != null && body != null && relativeUrl != null && tags != null)
            {
                // Validate
                if (title.Length < TITLE_MIN || title.Length > TITLE_MAX)
                    error = "Title must be " + TITLE_MIN + " to " + TITLE_MAX + " characters in length!";
                else if (body.Length < BODY_MIN || body.Length > BODY_MAX)
                    error = "Body must be " + BODY_MIN + " to " + BODY_MAX + " characters in length!";
                else if ((error = validRelativeUrl(relativeUrl)) != null)
                    ;
                else
                {
                    // Verify tags
                    ArticleTags parsedTags = getTags(tags);
                    if (parsedTags.error != null) error = parsedTags.error;
                    else
                    {
                        // Posted data is valid, check if the thread exists - else create it
                        bool updateArticle = false; // If the article is being modified and it has not been published and it's owned by the same user -> update it (user may make a small change)
                        string threadid;
                        Result threadCheck = conn.Query_Read("SELECT threadid FROM articles_thread WHERE relative_url='" + Utils.Escape(relativeUrl) + "'");
                        if (threadCheck.Rows.Count == 1)
                        {
                            // -- Thread exists
                            threadid = threadCheck[0]["threadid"];
                            // -- Check if to update the article if the articleid has been specified
                            if (articleid != null)
                            {
                                Result updateCheck = conn.Query_Read("SELECT userid, published FROM articles WHERE articleid='" + Utils.Escape(articleid) + "' AND threadid='" + Utils.Escape(threadid) + "'");
                                if (updateCheck.Rows.Count == 1 && updateCheck[0]["userid"] == HttpContext.Current.User.Identity.Name && !updateCheck[0]["published"].Equals("1"))
                                    updateArticle = true;
                            }
                        }
                        else
                            // -- Create thread
                            threadid = conn.Query_Scalar("INSERT INTO articles_thread (relative_url) VALUES('" + Utils.Escape(relativeUrl) + "'); SELECT LAST_INSERT_ID();").ToString();
                        
                        // Check if to insert or update the article
                        if (updateArticle)
                        {
                            StringBuilder query = new StringBuilder();
                            // Update the article
                            query
                                .Append("UPDATE articles SET title='").Append(title)
                                .Append("', body='").Append(body)
                                .Append("', allow_comments='").Append(allowComments ? "1" : "0")
                                .Append("', allow_html='").Append(allowHTML ? "1" : "0")
                                .Append("', show_pane='").Append(showPane).Append("' WHERE articleid='").Append(articleid).Append("';");
                            // Delete the previous tags
                            query.Append("DELETE FROM articles_tags_article WHERE articleid='" + Utils.Escape(articleid) + "';");
                            // -- Execute query
                            conn.Query_Execute(query.ToString());
                        }
                        else
                        {
                            // Check if the user is able to publish articles, if so we'll just publish it automatically
                            Result userPerm = conn.Query_Read("SELECT ug.access_media_publish FROM bsa_users AS u LEFT OUTER JOIN bsa_user_groups AS ug ON ug.groupid=u.groupid WHERE u.userid='" + Utils.Escape(HttpContext.Current.User.Identity.Name) + "'");
                            if (userPerm.Rows.Count != 1) return; // Something is critically wrong with basic-site-auth
                            bool publishAuto = userPerm[0]["access_media_publish"].Equals("1"); // If true, this will also set the new article as the current article for the thread
                            // Insert article and link to the thread
                            StringBuilder query = new StringBuilder();
                            query
                                .Append("INSERT INTO articles (threadid, title, userid, body, moderator_userid, published, allow_comments, allow_html, show_pane, datetime) VALUES('")
                                .Append(Utils.Escape(threadid))
                                .Append("', '").Append(Utils.Escape(title))
                                .Append("', '").Append(Utils.Escape(HttpContext.Current.User.Identity.Name))
                                .Append("', '").Append(Utils.Escape(body))
                                .Append("', ").Append(publishAuto ? "'" + Utils.Escape(HttpContext.Current.User.Identity.Name) + "'" : "NULL")
                                .Append(", '").Append(publishAuto ? "1" : "0")
                                .Append("', '").Append(allowComments ? "1" : "0")
                                .Append("', '").Append(allowHTML ? "1" : "0")
                                .Append("', '").Append(showPane ? "1" : "0")
                                .Append("', NOW()); SELECT LAST_INSERT_ID();");
                            articleid = conn.Query_Scalar(query.ToString()).ToString();
                            // If this was automatically published, set it as the current article for the thread
                            if (publishAuto)
                                conn.Query_Execute("UPDATE articles_thread SET articleid_current='" + Utils.Escape(articleid) + "' WHERE relative_url='" + Utils.Escape(relativeUrl) + "'");
                        }
                        // Add the new tags and delete any tags not used by any other articles
                        StringBuilder finalQuery = new StringBuilder();
                        if(parsedTags.tags.Count > 0)
                        {
                            StringBuilder tagsInsertQuery = new StringBuilder();
                            StringBuilder tagsArticleQuery = new StringBuilder();
                            foreach(string tag in parsedTags.tags)
                            {
                                // -- Attempt to insert the tags - if they exist, they wont be inserted
                                tagsInsertQuery.Append("('" + Utils.Escape(tag) + "'),");
                                tagsArticleQuery.Append("((SELECT tagid FROM articles_tags WHERE keyword='" + Utils.Escape(tag) + "'), '" + Utils.Escape(articleid) + "'),");
                            }
                            // -- Build final query
                            
                            finalQuery.Append("INSERT IGNORE INTO articles_tags (keyword) VALUES")
                                .Append(tagsInsertQuery.Remove(tagsInsertQuery.Length - 1, 1).ToString())
                                .Append("; INSERT IGNORE INTO articles_tags_article (tagid, articleid) VALUES")
                                .Append(tagsArticleQuery.Remove(tagsArticleQuery.Length - 1, 1).ToString())
                                .Append(";");                            
                        }
                        // -- This will delete any tags in the main table no longer used in the articles tags table
                        finalQuery.Append("DELETE FROM articles_tags WHERE NOT EXISTS (SELECT DISTINCT tagid FROM articles_tags_article WHERE tagid=articles_tags.tagid);");
                        // -- Execute final query
                        conn.Query_Execute(finalQuery.ToString());
                        // Redirect to the new article
                        conn.Disconnect();
                        response.Redirect(pageElements["URL"] + "/article/" + articleid, true);
                    }
                }
            }
            // Display form
            pageElements["CONTENT"] = Core.templates["articles"]["editor"]
                .Replace("%ERROR%", error != null ? Core.templates[baseTemplateParent]["error"].Replace("%ERROR%", HttpUtility.HtmlEncode(error)) : string.Empty)
                .Replace("%PARAMS%", preData != null ? "articleid=" + HttpUtility.UrlEncode(preData[0]["articleid"]) : string.Empty)
                .Replace("%TITLE%", HttpUtility.HtmlEncode(title ?? (preDataRow != null ? preDataRow["title"] : string.Empty)))
                .Replace("%RELATIVE_PATH%", HttpUtility.HtmlEncode(relativeUrl ?? (preDataRow != null ? preDataRow["relative_url"] : string.Empty)))
                .Replace("%TAGS%", HttpUtility.HtmlEncode(tags ?? (preDataRow != null ? preDataRow["tags"] : string.Empty)))
                .Replace("%ALLOW_HTML%", allowHTML ? "checked" : string.Empty)
                .Replace("%ALLOW_COMMENTS%", allowComments ? "checked" : string.Empty)
                .Replace("%SHOW_PANE%", showPane ? "checked" : string.Empty)
                .Replace("%BODY%", HttpUtility.HtmlEncode(body ?? (preDataRow != null ? preDataRow["body"] : string.Empty)))
                ;
            pageElements["TITLE"] = "Articles - Editor";
        }
        public static void pageArticle_Delete(string pluginid, Connector conn, ref Misc.PageElements pageElements, HttpRequest request, HttpResponse response, ref string baseTemplateParent)
        {
        }
        public static void handleRequestNotFound(string pluginid, Connector conn, ref Misc.PageElements pageElements, HttpRequest request, HttpResponse response, ref string baseTemplateParent)
        {
            if(Core.settings[SETTINGS_KEY].getBool(SETTINGS_KEY_HANDLES_404))
                pageArticle_View(pluginid, conn, ref pageElements, request, response, ref baseTemplateParent);
        }

        /// <summary>
        /// Validates a URL is relative; path should be in the format of e.g.:
        /// path
        /// path/path
        /// path/path/path
        /// 
        /// Allowed characters:
        /// - Alpha-numeric
        /// - Under-scroll
        /// - Dot/period
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string validRelativeUrl(string text)
        {
            if (text == null) return "Invalid relative path!";
            else if (text.Length == 0) return "No relative URL specified!";
            else if (text.StartsWith("/")) return "Relative path cannot start with '/'...";
            else if (text.EndsWith("/")) return "Relative path cannot end with '/'...";
            string[] chunks = text.Split('/');
            if (chunks.Length > RELATIVE_URL_MAXCHUNKS) return "Max top-directories in relative-path exceeded!";
            foreach (string s in chunks)
            {
                if (s.Length < RELATIVE_URL_CHUNK_MIN || s.Length > RELATIVE_URL_CHUNK_MAX)
                    return "Relative URL folder '" + s + "' must be " + RELATIVE_URL_CHUNK_MIN + " to " + RELATIVE_URL_CHUNK_MAX + " characters in size!";
                else
                    foreach (char c in s)
                    {
                        if ((c < 48 && c > 57) && (c < 65 && c > 90) && (c < 97 && c > 122) && c != 95 && c != 46)
                            return "Invalid character '" + c + "'!";
                    }
            }
            return null;
        }
        /// <summary>
        /// Parses a string such as "web,cms,ubermeat" (without quotations) to extract tags; the return structure
        /// will contain an array of successfully parsed tags; however if an error occurs, the error variable
        /// is set and the process is aborted.
        /// </summary>
        /// <param name="tags"></param>
        /// <returns></returns>
        public static ArticleTags getTags(string tags)
        {
            // Initialize return struct
            ArticleTags tagCollection = new ArticleTags();
            tagCollection.error = null;
            tagCollection.tags = new List<string>();
            // Parse the tags and try to find an error
            string tag;
            foreach (string rawTag in tags.Split(','))
            {
                tag = rawTag.Trim();
                if (tag.Length != 0)
                {
                    if (tag.Length < TAGS_TITLE_MIN || tag.Length > TAGS_TITLE_MAX)
                    {
                        tagCollection.error = "Invalid tag '" + tag + "' - must be between " + TAGS_TITLE_MIN + " to " + TAGS_TITLE_MAX + " characters!";
                        break;
                    }
                    else if (tagCollection.tags.Count + 1 > TAGS_MAX)
                        tagCollection.error = "Maximum tags of " + TAGS_MAX + " exceeded!";
                    else
                        tagCollection.tags.Add(tag);
                }
            }
            return tagCollection;
        }
        public struct ArticleTags
        {
            public List<string> tags;
            public string error;
        }
    }
}