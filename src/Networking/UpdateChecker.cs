namespace Vellum.Networking
{
    using System;
    using System.IO;
    using System.Net;
    using System.Reflection;
    using System.Text;
    using System.Text.RegularExpressions;

    using Newtonsoft.Json;

    internal enum ReleaseProvider
    {
        GITHUB_RELEASES,

        HTML,
    }

    internal enum VersionFormatting
    {
        MAJOR_MINOR_REVISION,

        MAJOR_MINOR_REVISION_BUILD,

        MAJOR_MINOR_BUILD_REVISION,
    }

    internal class UpdateChecker
    {
        private const ushort _timeout = 3000;

        private readonly string _apiUrl;

        private readonly string _regex;

        public UpdateChecker(ReleaseProvider provider, string apiUrl, string regex)
        {
            Provider = provider;
            _apiUrl = apiUrl;
            _regex = regex;
        }

        public Version RemoteVersion { get; private set; } = Assembly.GetExecutingAssembly().GetName().Version;

        public ReleaseProvider Provider { get; }

        public static string ParseVersion(Version version, VersionFormatting formatting)
        {
            var builder = new StringBuilder();

            switch (formatting)
            {
                case VersionFormatting.MAJOR_MINOR_REVISION:
                    builder.Append($"{version.Major}.{version.Minor}.{version.Revision}");
                    break;

                case VersionFormatting.MAJOR_MINOR_REVISION_BUILD:
                    builder.Append($"{version.Major}.{version.Minor}.{version.Revision}.{version.Build}");
                    break;

                case VersionFormatting.MAJOR_MINOR_BUILD_REVISION:
                    builder.Append(version);
                    break;
            }

            return builder.ToString();
        }

        public static Version ParseVersion(string version, VersionFormatting formatting)
        {
            var formattedVersion = new Version();
            var matches = Regex.Matches(version, @"(\d+)");

            var result = false;

            switch (formatting)
            {
                case VersionFormatting.MAJOR_MINOR_REVISION:
                    if (matches.Count == 3)
                    {
                        formattedVersion = new Version(Convert.ToInt32(matches[0].Captures[0].Value), Convert.ToInt32(matches[1].Captures[0].Value), 0, Convert.ToInt32(matches[2].Captures[0].Value));
                        result = true;
                    }

                    break;

                case VersionFormatting.MAJOR_MINOR_REVISION_BUILD:
                    if (matches.Count == 4)
                    {
                        formattedVersion = new Version(
                            Convert.ToInt32(matches[0].Captures[0].Value),
                            Convert.ToInt32(matches[1].Captures[0].Value),
                            Convert.ToInt32(matches[3].Captures[0].Value),
                            Convert.ToInt32(matches[2].Captures[0].Value));
                        result = true;
                    }

                    break;

                case VersionFormatting.MAJOR_MINOR_BUILD_REVISION:
                    break;
            }

            if (!result)
            {
                throw new ArgumentException($"\"{version}\" could not be parsed into \"{Enum.GetName(typeof(VersionFormatting), formatting)}\" format.");
            }

            return formattedVersion;
        }

        /// <summary>Compares two versions with each other.</summary>
        /// <returns>
        ///     Returns <c>-1</c> if the <c>primary</c> version is older than the <c>secondary</c> version, <c>0</c> if equal
        ///     or <c>1</c> if newer.
        /// </returns>
        public static short CompareVersions(Version primary, Version secondary, VersionFormatting formatting)
        {
            Version[] versions = { primary, secondary };
            var versionNumbers = new int[2, 4];

            switch (formatting)
            {
                case VersionFormatting.MAJOR_MINOR_REVISION_BUILD:
                    versionNumbers = new int[2, 4] { { primary.Major, primary.Minor, primary.Revision, primary.Build }, { secondary.Major, secondary.Minor, secondary.Revision, secondary.Build } };
                    break;

                case VersionFormatting.MAJOR_MINOR_BUILD_REVISION:
                    versionNumbers = new int[2, 4] { { primary.Major, primary.Minor, primary.Build, primary.Revision }, { secondary.Major, secondary.Minor, secondary.Build, secondary.Revision } };
                    break;

                case VersionFormatting.MAJOR_MINOR_REVISION:
                    versionNumbers = new int[2, 3] { { primary.Major, primary.Minor, primary.Revision }, { secondary.Major, secondary.Minor, secondary.Revision } };
                    break;
            }

            // Compare versions
            for (var i = 0; i < versionNumbers.GetLength(1); i++)
            {
                if (versionNumbers[0, i] < versionNumbers[1, i])
                    return -1;
                if (versionNumbers[0, i] > versionNumbers[1, i])
                    return 1;
            }

            return 0;
        }

        public bool GetLatestVersion()
        {
            var result = false;

            var req = WebRequest.CreateHttp(_apiUrl);
            req.Timeout = _timeout;
            req.UserAgent = Assembly.GetExecutingAssembly().GetName().Name;

            switch (Provider)
            {
                case ReleaseProvider.GITHUB_RELEASES:
                    try
                    {
                        var resp = (HttpWebResponse)req.GetResponse();

                        using (var streamReader = new StreamReader(resp.GetResponseStream()))
                        {
                            var versionTag = (string)JsonConvert.DeserializeObject<dynamic>(streamReader.ReadToEnd())["tag_name"];
                            var versionMatch = Regex.Match(versionTag, _regex);

                            if (versionMatch.Groups.Count >= 3)
                            {
                                RemoteVersion = new Version(
                                    Convert.ToInt32(versionMatch.Groups[1].Value),
                                    Convert.ToInt32(versionMatch.Groups[2].Value),
                                    0,
                                    Convert.ToInt32(versionMatch.Groups[3].Value));
                                result = true;
                            }
                        }

                        resp.Close();
                    }
                    catch
                    {
                        result = false;
                    }

                    break;

                case ReleaseProvider.HTML:
                    //HttpWebRequest request = WebRequest.CreateHttp(_apiUrl);
                    try
                    {
                        var resp = (HttpWebResponse)req.GetResponse();

                        using (var reader = new StreamReader(resp.GetResponseStream()))
                        {
                            var match = Regex.Match(reader.ReadToEnd(), _regex);

                            if (match.Groups.Count > 1)
                            {
                                RemoteVersion = ParseVersion(match.Groups[1].Value, VersionFormatting.MAJOR_MINOR_REVISION_BUILD);
                                result = true;
                            }
                            else
                            {
                                result = false;
                            }
                        }
                    }
                    catch
                    {
                        result = false;
                    }

                    break;
            }

            return result;
        }
    }
}
