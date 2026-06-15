using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using Redmine.Net.Api.Types;
using Redmine.Net.Api;

namespace Redmine.Client
{
    /// <summary>A single id+name reference (project, status, tracker, ...) as stored in the cache.</summary>
    public class CachedRef
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// The subset of an <see cref="Issue"/> that the issue grid actually displays. The Redmine
    /// API type cannot be serialized faithfully (its WriteXml only persists the *_id fields and
    /// drops the display names), so we round-trip through this DTO instead.
    /// </summary>
    public class CachedIssue
    {
        public int Id { get; set; }
        public string Subject { get; set; }
        public CachedRef Project { get; set; }
        public CachedRef Tracker { get; set; }
        public CachedRef Status { get; set; }
        public CachedRef Priority { get; set; }
        public CachedRef Category { get; set; }
        public CachedRef AssignedTo { get; set; }
        public CachedRef FixedVersion { get; set; }
        public CachedRef ParentIssue { get; set; }
    }

    /// <summary>
    /// On-disk cache of the issue list, keyed by the query (project + filter). Lets the UI show
    /// the last-known issues instantly on startup while the (server-bound, multi-second) refresh
    /// runs in the background. Best-effort: any failure silently falls back to a live fetch.
    /// </summary>
    internal static class IssueCache
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(List<CachedIssue>));

        private static string Dir
        {
            get
            {
                string dir = Path.Combine(System.Windows.Forms.Application.CommonAppDataPath, "IssueCache");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>A filesystem-safe key identifying the exact query whose issues are cached.</summary>
        public static string BuildKey(int projectId, bool onlyMe, Filter filter)
        {
            return string.Format("p{0}_me{1}_t{2}_s{3}_pr{4}_v{5}_c{6}_a{7}_q{8}",
                projectId, onlyMe ? 1 : 0, filter.TrackerId, filter.StatusId, filter.PriorityId,
                filter.VersionId, filter.CategoryId, filter.AssignedToId, Sanitize(filter.Subject));
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            return sb.ToString();
        }

        public static IList<Issue> TryLoad(string key)
        {
            try
            {
                string file = Path.Combine(Dir, key + ".xml");
                if (!File.Exists(file))
                    return null;
                using (FileStream fs = File.OpenRead(file))
                {
                    List<CachedIssue> cached = (List<CachedIssue>)Serializer.Deserialize(fs);
                    List<Issue> result = new List<Issue>(cached.Count);
                    foreach (CachedIssue ci in cached)
                        result.Add(ToIssue(ci));
                    return result;
                }
            }
            catch
            {
                return null; // a broken/old cache file is not worth crashing over
            }
        }

        public static void Save(string key, IEnumerable<Issue> issues)
        {
            try
            {
                List<CachedIssue> list = new List<CachedIssue>();
                foreach (Issue i in issues)
                    list.Add(ToCached(i));
                string file = Path.Combine(Dir, key + ".xml");
                using (FileStream fs = File.Create(file))
                    Serializer.Serialize(fs, list);
            }
            catch { /* cache writes are best-effort */ }
        }

        private static CachedRef Ref(IdentifiableName n)
        {
            return n == null ? null : new CachedRef { Id = n.Id, Name = n.Name };
        }

        private static IdentifiableName Name(CachedRef r)
        {
            return r == null ? null : new IdentifiableName { Id = r.Id, Name = r.Name };
        }

        private static CachedIssue ToCached(Issue i)
        {
            return new CachedIssue
            {
                Id = i.Id,
                Subject = i.Subject,
                Project = Ref(i.Project),
                Tracker = Ref(i.Tracker),
                Status = Ref(i.Status),
                Priority = Ref(i.Priority),
                Category = Ref(i.Category),
                AssignedTo = Ref(i.AssignedTo),
                FixedVersion = Ref(i.FixedVersion),
                ParentIssue = Ref(i.ParentIssue),
            };
        }

        private static Issue ToIssue(CachedIssue c)
        {
            return new Issue
            {
                Id = c.Id,
                Subject = c.Subject,
                Project = Name(c.Project),
                Tracker = Name(c.Tracker),
                Status = Name(c.Status),
                Priority = Name(c.Priority),
                Category = Name(c.Category),
                AssignedTo = Name(c.AssignedTo),
                FixedVersion = Name(c.FixedVersion),
                ParentIssue = Name(c.ParentIssue),
            };
        }
    }

    public class ClientProject : Project
    {
        public ClientProject(Project p) {
            this.Id = p.Id;
            this.Name = p.Name;
            this.Identifier = p.Identifier;
            this.Description = p.Description;
            this.Parent = p.Parent;
            this.HomePage = p.HomePage;
            this.CreatedOn = p.CreatedOn;
            this.UpdatedOn = p.UpdatedOn;
            this.Trackers = p.Trackers;
            this.CustomFields = p.CustomFields;
        }

        public string DisplayName {
            get {
                if (Parent != null)
                    return Parent.Name + " - " + Name;
                return Name;
            }
        }
    }

    // The enum order matters: it is compared with >= throughout the code to gate features by
    // minimum server version, and the *integer index* is what gets persisted as the ApiVersion
    // setting. Therefore only ever APPEND new members - never reorder or remove - or existing
    // user.config values would silently map to the wrong version.
    //
    // The client only changes behaviour at four breakpoints: V13x (issues/trackers/statuses),
    // V14x (project memberships), V22x (server-side priorities/activities) and V24x (custom
    // fields). There is no upper-bound check anywhere, so every server from 2.4 onwards behaves
    // identically. The entries past V24x are purely so the picker can reflect the user's actual
    // server version; they need no new feature gates.
    public enum ApiVersion
    {
        V10x,
        V11x,
        V12x,
        V13x,
        V14x,
        V20x,
        V21x,
        V22x,
        V23x,
        V24x,
        V25x,
        V30x,
        V40x,
        V50x,
        V60x,
    }

    public class Filter : ICloneable
    {
        public int TrackerId = 0;
        public int StatusId = 0;
        public int PriorityId = 0;
        public string Subject = "";
        public int AssignedToId = 0;
        public int VersionId = 0;
        public int CategoryId = 0;

        #region ICloneable Members

        public object Clone()
        {
            return new Filter { TrackerId = TrackerId, StatusId = StatusId, PriorityId = PriorityId, Subject = Subject, AssignedToId = AssignedToId, VersionId = VersionId, CategoryId = CategoryId };
        }

        #endregion ICloneable Members
    }

    internal class LoadException : Exception
    {
        public LoadException(String action, Exception innerException) : base(action, innerException)
{

}
    }

    internal class MainFormData
    {
        public List<ClientProject> Projects { get; private set; }
        public IList<Issue> Issues { get; set; }
        public IList<CustomField> CustomFields { get; private set; }

        // search data
        public List<ProjectTracker> Trackers { get; private set; }

        public List<IssueCategory> Categories { get; private set; }
        public List<IssueStatus> Statuses { get; private set; }
        public List<Redmine.Net.Api.Types.Version> Versions { get; private set; }
        public List<ProjectMember> ProjectMembers { get; private set; }
        public List<Enumerations.EnumerationItem> IssuePriorities { get; private set; }
        public List<Enumerations.EnumerationItem> Activities { get; private set; }
        public int ProjectId { get; }

        public MainFormData(IList<Project> projects, int projectId, bool onlyMe, Filter filter, IList<Issue> preloadedIssues = null)
        {
            ProjectId = projectId;
            Projects = new List<ClientProject>();
            Projects.Add(new ClientProject(new Project { Id = -1, Name = Languages.Lang.ShowAllIssues }));
            foreach(Project p in projects)
            {
                Projects.Add(new ClientProject(p));
            }
            if (RedmineClientForm.RedmineVersion >= ApiVersion.V13x)
            {
                if (projectId < 0)
                {
                    try
                    {
                        List<Tracker> allTrackers = (List<Tracker>)RedmineClientForm.redmine.GetObjects<Tracker>();
                        Trackers = allTrackers.ConvertAll(new Converter<Tracker, ProjectTracker>(TrackerToProjectTracker));
                    }
                    catch (Exception e)
                    {
                        throw new LoadException(Languages.Lang.BgWork_LoadTrackers, e);
                    }
                    Categories = null;
                    Versions = null;
                }
                else
                {
                    try
                    {
                        NameValueCollection projectParameters = new NameValueCollection { { "include", "trackers" } };
                        Project project = RedmineClientForm.redmine.GetObject<Project>(projectId.ToString(), projectParameters);
                        Trackers = new List<ProjectTracker>(project.Trackers);
                    }
                    catch (Exception e)
                    {
                        throw new LoadException(Languages.Lang.BgWork_LoadProjectTrackers, e);
                    }

                    try
                    {
                        Categories = new List<IssueCategory>(RedmineClientForm.redmine.GetObjects<IssueCategory>(InitParameters()));
                        Categories.Insert(0, new IssueCategory { Id = 0, Name = "" });
                    }
                    catch (Exception e)
                    {
                        throw new LoadException(Languages.Lang.BgWork_LoadCategories, e);
                    }

                    try
                    {
                        Versions = (List<Redmine.Net.Api.Types.Version>)RedmineClientForm.redmine.GetObjects<Redmine.Net.Api.Types.Version>(InitParameters());
                        Versions.Insert(0, new Redmine.Net.Api.Types.Version { Id = 0, Name = "" });
                    }
                    catch (Exception e)
                    {
                        throw new LoadException(Languages.Lang.BgWork_LoadVersions, e);
                    }
                }
                Trackers.Insert(0, new ProjectTracker { Id = 0, Name = "" });

                try
                {
                    Statuses = new List<IssueStatus>(RedmineClientForm.redmine.GetObjects<IssueStatus>(InitParameters()));
                    Statuses.Insert(0, new IssueStatus { Id = 0, Name = Languages.Lang.AllOpenIssues });
                    Statuses.Add(new IssueStatus { Id = -1, Name = Languages.Lang.AllClosedIssues });
                    Statuses.Add(new IssueStatus { Id = -2, Name = Languages.Lang.AllOpenAndClosedIssues });
                }
                catch (Exception e)
                {
                    throw new LoadException(Languages.Lang.BgWork_LoadStatuses, e);
                }

                try
                {
                    if (RedmineClientForm.RedmineVersion >= ApiVersion.V14x
                        && projectId > 0)
                    {
                        List<ProjectMembership> projectMembers = (List<ProjectMembership>)RedmineClientForm.redmine.GetObjects<ProjectMembership>(InitParameters());
                        ProjectMembers = projectMembers.ConvertAll(new Converter<ProjectMembership, ProjectMember>(ProjectMember.MembershipToMember));
                    }
                    else
                    {
                        List<User> allUsers = (List<User>)RedmineClientForm.redmine.GetObjects<User>();
                        ProjectMembers = allUsers.ConvertAll(new Converter<User, ProjectMember>(UserToProjectMember));
                    }
                    ProjectMembers.Insert(0, new ProjectMember());
                }
                catch (Exception)
                {
                    ProjectMembers = null;
                    //throw new LoadException(Languages.Lang.BgWork_LoadProjectMembers, e);
                }

                try
                {
                    if (RedmineClientForm.RedmineVersion >= ApiVersion.V22x)
                    {
                        Enumerations.UpdateIssuePriorities(RedmineClientForm.redmine.GetObjects<IssuePriority>());
                        Enumerations.SaveIssuePriorities();

                        Enumerations.UpdateActivities(RedmineClientForm.redmine.GetObjects<TimeEntryActivity>());
                        Enumerations.SaveActivities();
                    }
                    IssuePriorities = new List<Enumerations.EnumerationItem>(Enumerations.IssuePriorities);
                    IssuePriorities.Insert(0, new Enumerations.EnumerationItem { Id = 0, Name = "", IsDefault = false });

                    Activities = new List<Enumerations.EnumerationItem>(Enumerations.Activities);
                    Activities.Insert(0, new Enumerations.EnumerationItem { Id = 0, Name = "", IsDefault = false });
                }
                catch (Exception e)
                {
                    throw new LoadException(Languages.Lang.BgWork_LoadPriorities, e);
                }

                try
                {
                    if (RedmineClientForm.RedmineVersion >= ApiVersion.V24x)
                    {
                        CustomFields = RedmineClientForm.redmine.GetObjects<CustomField>();
                    }
                }
                catch (Exception e)
                {
                    throw new LoadException(Languages.Lang.BgWork_LoadCustomFields, e);
                }
            }

            try
            {
                // preloadedIssues != null means the caller supplied issues (e.g. from the cache,
                // possibly an empty list on a cache miss) and a live fetch should be skipped here -
                // it happens afterwards as a background refresh.
                Issues = preloadedIssues ?? LoadIssuesFromServer(projectId, onlyMe, filter);
            }
            catch (Exception e)
            {
                throw new LoadException(Languages.Lang.BgWork_LoadIssues, e);
            }
        }

        /// <summary>
        /// Build the issue-list query parameters (project + filter) for a given view.
        /// </summary>
        private static NameValueCollection BuildIssueParameters(int projectId, bool onlyMe, Filter filter)
        {
            NameValueCollection parameters = new NameValueCollection();
            if (projectId != -1)
                parameters.Add(RedmineKeys.PROJECT_ID, projectId.ToString());

            if (onlyMe)
                parameters.Add(RedmineKeys.ASSIGNED_TO_ID, "me");
            else if (filter.AssignedToId > 0)
                parameters.Add(RedmineKeys.ASSIGNED_TO_ID, filter.AssignedToId.ToString());

            if (filter.TrackerId > 0)
                parameters.Add(RedmineKeys.TRACKER_ID, filter.TrackerId.ToString());

            if (filter.StatusId > 0)
                parameters.Add(RedmineKeys.STATUS_ID, filter.StatusId.ToString());
            else if (filter.StatusId < 0)
            {
                switch (filter.StatusId)
                {
                    case -1: // all closed issues
                        parameters.Add(RedmineKeys.STATUS_ID, "closed");
                        break;

                    case -2: // all open and closed issues
                        parameters.Add(RedmineKeys.STATUS_ID, " *");
                        break;
                }
            }

            if (filter.PriorityId > 0)
                parameters.Add(RedmineKeys.PRIORITY_ID, filter.PriorityId.ToString());

            if (filter.VersionId > 0)
                parameters.Add(RedmineKeys.FIXED_VERSION_ID, filter.VersionId.ToString());

            if (filter.CategoryId > 0)
                parameters.Add(RedmineKeys.CATEGORY_ID, filter.CategoryId.ToString());

            if (!String.IsNullOrEmpty(filter.Subject))
                parameters.Add(RedmineKeys.SUBJECT, "~" + filter.Subject);

            return parameters;
        }

        /// <summary>
        /// Fetch the issues for a view straight from the server (the slow path - see
        /// <see cref="GetIssuesParallel"/>). Exposed so the form can refresh issues in the
        /// background after showing the cached list.
        /// </summary>
        public static IList<Issue> LoadIssuesFromServer(int projectId, bool onlyMe, Filter filter)
        {
            return GetIssuesParallel(BuildIssueParameters(projectId, onlyMe, filter));
        }

        /// <summary>
        /// Fetch every issue matching <paramref name="baseParameters"/>. The Redmine REST
        /// API returns at most 100 issues per request, so the full "all projects" list takes
        /// many requests. The default GetObjects&lt;Issue&gt;() walks the pages one-by-one,
        /// paying the full request latency for each - dozens of serial round-trips for a large
        /// instance. Instead, fetch the first page (which also reports the total count), then
        /// pull the remaining pages concurrently. Filtered/per-project queries usually fit in a
        /// single page and skip the parallel path entirely.
        /// </summary>
        private static IList<Issue> GetIssuesParallel(NameValueCollection baseParameters)
        {
            const int pageSize = 100;

            NameValueCollection firstParams = new NameValueCollection(baseParameters);
            firstParams.Set(RedmineKeys.LIMIT, pageSize.ToString());
            firstParams.Set(RedmineKeys.OFFSET, "0");
            PaginatedObjects<Issue> first = RedmineClientForm.redmine.GetPaginatedObjects<Issue>(firstParams);

            List<Issue> result = new List<Issue>(first.Objects);
            int total = first.TotalCount;
            if (total <= pageSize || first.Objects.Count == 0)
                return result;

            // Offsets of the pages still to fetch: pageSize, 2*pageSize, ... < total.
            List<int> offsets = new List<int>();
            for (int offset = pageSize; offset < total; offset += pageSize)
                offsets.Add(offset);

            // Fetch them in parallel, keeping each page in its slot so the final list stays
            // in server order. Each task gets its own parameter copy (NameValueCollection is
            // not thread-safe and the offset differs per page).
            List<Issue>[] pages = new List<Issue>[offsets.Count];
            System.Threading.Tasks.Parallel.For(0, offsets.Count,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 8 },
                i =>
                {
                    NameValueCollection p = new NameValueCollection(baseParameters);
                    p.Set(RedmineKeys.LIMIT, pageSize.ToString());
                    p.Set(RedmineKeys.OFFSET, offsets[i].ToString());
                    pages[i] = RedmineClientForm.redmine.GetPaginatedObjects<Issue>(p).Objects;
                });

            foreach (List<Issue> page in pages)
            {
                if (page != null)
                    result.AddRange(page);
            }
            return result;
        }

        private NameValueCollection InitParameters()
        {
            NameValueCollection parameters = new NameValueCollection();
            if (ProjectId != -1)
                parameters.Add(RedmineKeys.PROJECT_ID, ProjectId.ToString());
            return parameters;
        }

        private static ProjectTracker TrackerToProjectTracker(Tracker tracker)
        {
            return new ProjectTracker { Id = tracker.Id, Name = tracker.Name };
        }

        private static ProjectMember UserToProjectMember(User user)
        {
            return new ProjectMember(user);
        }

        public static Dictionary<int, T> ToDictionaryId<T>(IList<T> list) where T : Identifiable<T>, System.IEquatable<T>
        {
            Dictionary<int, T> dict = new Dictionary<int,T>();
            foreach (T element in list)
            {
                dict.Add(element.Id, element);
            }
            return dict;
        }

        public static Dictionary<int, Y> ToDictionaryName<Y>(IList<Y> list) where Y : IdentifiableName
        {
            Dictionary<int, Y> dict = new Dictionary<int, Y>();
            foreach (Y element in list)
            {
                dict.Add(element.Id, element);
            }
            return dict;
        }
    }
}
