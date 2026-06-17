using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Redmine.Net.Api.Types;
using Redmine.Net.Api;
using Redmine.Net.Api.Serialization;
// Redmine.Net.Api.Types now also defines a File type; disambiguate the BCL one we use here.
using File = System.IO.File;

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
                List<CachedIssue> cached;
                using (FileStream fs = File.OpenRead(file))
                    cached = (List<CachedIssue>)Serializer.Deserialize(fs);

                // The modern Issue has read-only Id, so we can't populate one by hand. Instead
                // rebuild the Redmine-format XML the server would have sent and let Issue's own
                // (public, IXmlSerializable) ReadXml reconstruct a fully-populated Issue. The
                // library's serializer types are internal, so we drive ReadXml directly.
                List<Issue> result = new List<Issue>(cached.Count);
                foreach (CachedIssue ci in cached)
                    result.Add(FromXml(ToXml(ci)));
                return result;
            }
            catch
            {
                return null; // a broken/old/incompatible cache file is not worth crashing over
            }
        }

        /// <summary>Reconstructs an immutable Issue from &lt;issue&gt; XML via its public ReadXml.</summary>
        private static Issue FromXml(string xml)
        {
            Issue issue = new Issue();
            using (XmlReader reader = XmlReader.Create(new StringReader(xml)))
            {
                reader.MoveToContent();   // position on <issue>; ReadXml does reader.Read() to enter
                issue.ReadXml(reader);
            }
            return issue;
        }

        /// <summary>Renders a cached issue as the &lt;issue&gt; XML the library's Issue.ReadXml expects.</summary>
        private static string ToXml(CachedIssue c)
        {
            StringBuilder sb = new StringBuilder();
            using (XmlWriter w = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
            {
                w.WriteStartElement("issue");
                w.WriteElementString("id", c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                w.WriteElementString("subject", c.Subject ?? "");
                WriteRef(w, "project", c.Project);
                WriteRef(w, "tracker", c.Tracker);
                WriteRef(w, "status", c.Status);
                WriteRef(w, "priority", c.Priority);
                WriteRef(w, "category", c.Category);
                WriteRef(w, "assigned_to", c.AssignedTo);
                WriteRef(w, "fixed_version", c.FixedVersion);
                WriteRef(w, "parent", c.ParentIssue);
                w.WriteEndElement();
            }
            return sb.ToString();
        }

        private static void WriteRef(XmlWriter w, string element, CachedRef r)
        {
            if (r == null)
                return;
            w.WriteStartElement(element);
            w.WriteAttributeString("id", r.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (r.Name != null)
                w.WriteAttributeString("name", r.Name);
            w.WriteEndElement();
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

    }

    // Standalone wrapper (no longer derives from Project, which the modern API sealed). It only
    // needs to expose what the project combo binds to: the Id (ValueMember) and DisplayName
    // (DisplayMember). Name/Parent are kept solely to build DisplayName.
    public class ClientProject
    {
        public int Id { get; private set; }
        public string Name { get; private set; }
        public IdentifiableName Parent { get; private set; }

        public ClientProject(Project p) {
            this.Id = p.Id;
            this.Name = p.Name;
            this.Parent = p.Parent;
        }

        // For synthetic entries (e.g. the "show all projects" pseudo-project) that have no
        // backing server Project. The modern API's Project is sealed and its Id read-only, so we
        // can't build a throwaway Project just to wrap it.
        public ClientProject(int id, string name) {
            this.Id = id;
            this.Name = name;
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
            Projects.Add(new ClientProject(-1, Languages.Lang.ShowAllIssues));
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
                        List<Tracker> allTrackers = (List<Tracker>)RedmineClientForm.redmine.Get<Tracker>();
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
                        Project project = RedmineClientForm.redmine.Get<Project>(projectId.ToString(), projectParameters.ToOptions());
                        Trackers = new List<ProjectTracker>(project.Trackers);
                    }
                    catch (Exception e)
                    {
                        throw new LoadException(Languages.Lang.BgWork_LoadProjectTrackers, e);
                    }

                    try
                    {
                        Categories = new List<IssueCategory>(RedmineClientForm.redmine.Get<IssueCategory>(InitParameters().ToOptions()));
                        Categories.Insert(0, new IssueCategory { Name = "" });
                    }
                    catch (Exception e)
                    {
                        throw new LoadException(Languages.Lang.BgWork_LoadCategories, e);
                    }

                    try
                    {
                        Versions = (List<Redmine.Net.Api.Types.Version>)RedmineClientForm.redmine.Get<Redmine.Net.Api.Types.Version>(InitParameters().ToOptions());
                        Versions.Insert(0, new Redmine.Net.Api.Types.Version { Name = "" });
                    }
                    catch (Exception e)
                    {
                        throw new LoadException(Languages.Lang.BgWork_LoadVersions, e);
                    }
                }
                Trackers.Insert(0, new ProjectTracker { Name = "" });

                try
                {
                    Statuses = new List<IssueStatus>(RedmineClientForm.redmine.Get<IssueStatus>(InitParameters().ToOptions()));
                    Statuses.Insert(0, new IssueStatus { Name = Languages.Lang.AllOpenIssues });
                    Statuses.Add(ClientExtensionMethods.NamedRef<IssueStatus>(-1, Languages.Lang.AllClosedIssues));
                    Statuses.Add(ClientExtensionMethods.NamedRef<IssueStatus>(-2, Languages.Lang.AllOpenAndClosedIssues));
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
                        List<ProjectMembership> projectMembers = (List<ProjectMembership>)RedmineClientForm.redmine.Get<ProjectMembership>(InitParameters().ToOptions());
                        ProjectMembers = projectMembers.ConvertAll(new Converter<ProjectMembership, ProjectMember>(ProjectMember.MembershipToMember));
                    }
                    else
                    {
                        List<User> allUsers = (List<User>)RedmineClientForm.redmine.Get<User>();
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
                        Enumerations.UpdateIssuePriorities(RedmineClientForm.redmine.Get<IssuePriority>());
                        Enumerations.SaveIssuePriorities();

                        Enumerations.UpdateActivities(RedmineClientForm.redmine.Get<TimeEntryActivity>());
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
                        CustomFields = RedmineClientForm.redmine.Get<CustomField>();
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
            PagedResults<Issue> first = RedmineClientForm.redmine.GetPaginated<Issue>(firstParams.ToOptions());

            List<Issue> result = new List<Issue>(first.Items);
            int total = first.TotalItems;
            if (total <= pageSize || result.Count == 0)
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
                    pages[i] = new List<Issue>(RedmineClientForm.redmine.GetPaginated<Issue>(p.ToOptions()).Items);
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
            return ClientExtensionMethods.NamedRef<ProjectTracker>(tracker.Id, tracker.Name);
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
