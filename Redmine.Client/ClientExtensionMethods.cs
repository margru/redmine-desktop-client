using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Redmine.Net.Api;
using Redmine.Net.Api.Net;
using Redmine.Net.Api.Serialization;
using Redmine.Net.Api.Types;
using System.Collections.Specialized;

namespace Redmine.Client
{
    public static class ClientExtensionMethods
    {
        /// <summary>
        /// Wraps a query-parameter collection as the modern API's RequestOptions. The old library
        /// took a NameValueCollection directly on Get/GetObjects; the new one takes RequestOptions
        /// whose QueryString carries the same parameters.
        /// </summary>
        public static RequestOptions ToOptions(this NameValueCollection parameters)
        {
            return parameters == null ? null : new RequestOptions { QueryString = parameters };
        }

        /// <summary>
        /// The modern API's list endpoints (<c>Get&lt;T&gt;(RequestOptions)</c>) return <c>null</c>
        /// - not an empty list - when there are no results, e.g. a project with no issue categories
        /// or no versions. Callers that build a <c>List&lt;T&gt;</c> from the result, cast it, or
        /// call <c>Insert</c>/<c>ConvertAll</c> on it would otherwise throw ArgumentNullException
        /// ("Value cannot be null. Parameter 'collection'") or NullReferenceException. This
        /// normalises null to an empty list.
        /// </summary>
        public static List<T> OrEmpty<T>(this List<T> source)
        {
            return source ?? new List<T>();
        }

        /// <summary>
        /// Creates an IdentifiableName-derived reference (IssueStatus, ProjectTracker, ...) with a
        /// given id and name. The modern API made Id read-only, so the old
        /// <c>new IssueStatus { Id = x, Name = y }</c> object initializers no longer compile;
        /// Id is set through the Create factory and Name assigned afterwards.
        /// </summary>
        public static T NamedRef<T>(int id, string name) where T : IdentifiableName, new()
        {
            T value = IdentifiableName.Create<T>(id);
            value.Name = name;
            return value;
        }

        /// <summary>Shorthand for an <see cref="IdentifiableName"/> with a given id and name.
        /// (The modern API's IdentifiableName(int,string) constructor is not public.)</summary>
        public static IdentifiableName Named(int id, string name)
        {
            return NamedRef<IdentifiableName>(id, name);
        }

        /// <summary>
        /// Builds a RedmineManager via the modern options-builder. Replaces the old
        /// <c>new RedmineManager(url[, user, password], mimeFormat)</c> constructors. Pass a null
        /// or empty login for anonymous access. Page size is baked into the connection options now
        /// (the Redmine REST API hard-caps the limit at 100).
        /// </summary>
        public static RedmineManager CreateManager(string host, string login, string password,
            SerializationType serialization, int pageSize = 100)
        {
            var builder = new RedmineManagerOptionsBuilder()
                .WithHost(host)
                .WithPageSize(pageSize)
                .WithSerializationType(serialization);
            if (!string.IsNullOrEmpty(login))
                builder = builder.WithBasicAuthentication(login, password);
            return new RedmineManager(builder);
        }

        public static void WriteCollectionAsElement<T>(this XmlWriter writer, IList<T> list, string listname) where T : class
        {
            var serializer = new XmlSerializer(typeof(T));
            writer.WriteStartElement(listname);
            foreach (T element in list)
            {
                serializer.Serialize(writer, element);
            }
            writer.WriteEndElement();
        }

        public static string CompleteName(this User user)
        {
            string completeName = "";
            if (!String.IsNullOrEmpty(user.FirstName))
                completeName += user.FirstName;
            if (!String.IsNullOrEmpty(user.LastName))
            {
                if (!String.IsNullOrEmpty(completeName))
                    completeName += " ";
                completeName += user.LastName;
            }
            return completeName;
        }

        public static String ToByteString(this long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + suf[place];
        }

        public static String ToByteString(this int byteCount)
        {
            long bytes = byteCount;
            return bytes.ToByteString();
        }

        public static void MoveControl(this System.Windows.Forms.Control control, int diffx, int diffy)
        {
            System.Drawing.Point loc = control.Location;
            loc.X += diffx;
            loc.Y += diffy;
            control.Location = loc;
        }

    }
}
