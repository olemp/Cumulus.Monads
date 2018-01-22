using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Web.Http.Description;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Graph;
using Pzl.O365.ProvisioningFunctions.Helpers;
using Group = Microsoft.Graph.Group;

namespace Pzl.O365.ProvisioningFunctions.Graph
{
    public static class CreateGroup
    {
        private static readonly Regex ReRemoveNonAlphaNumChars = new Regex("[^a-z0-9]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        [FunctionName("CreateGroup")]
        [ResponseType(typeof(CreateGroupResponse))]
        [Display(Name = "Create Office 365 Group", Description = "This action will create a new Office 365 Group")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "post")]CreateGroupRequest request, TraceWriter log)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    throw new ArgumentException("Parameter cannot be null", "Name");
                }
                if (string.IsNullOrWhiteSpace(request.Description))
                {
                    throw new ArgumentException("Parameter cannot be null", "Description");
                }
                string mailNickName = await GetUniqueMailAlias(request.Name, request.Prefix, request.UsePrefixInMailAlias);
                string displayName = GetDisplayName(request.Name, request.Prefix, request.UsePrefixInDisplayName);
                GraphServiceClient client = ConnectADAL.GetGraphClient();
                var newGroup = new Group
                {
                    DisplayName = displayName,
                    Description = GetDescription(request.Description, 1000),
                    MailNickname = mailNickName,
                    MailEnabled = true,
                    SecurityEnabled = false,
                    Visibility = request.Public ? "Public" : "Private",
                    GroupTypes = new List<string> { "Unified" },
                    Classification = request.Classification
                };
                var addedGroup = await client.Groups.Request().AddAsync(newGroup);
                var createGroupResponse = new CreateGroupResponse
                {
                    GroupId = addedGroup.Id,
                    DisplayName = displayName,
                    Mail = addedGroup.Mail
                };
                return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ObjectContent<CreateGroupResponse>(createGroupResponse, new JsonMediaTypeFormatter())
                });
            }
            catch (Exception e)
            {
                log.Error($"Error:  {e.Message }\n\n{e.StackTrace}");
                return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new ObjectContent<string>(e.Message, new JsonMediaTypeFormatter())
                });
            }
        }

        static string GetDisplayName(string name, string prefix, bool usePrefix)
        {
            //remove prefix from name if accidentally added as part of the name
            var displayName = Regex.Replace(name, prefix + @":?\s+", "", RegexOptions.IgnoreCase);

            if (!string.IsNullOrWhiteSpace(prefix) && usePrefix)
            {
                CultureInfo cultureInfo = Thread.CurrentThread.CurrentCulture;
                prefix = cultureInfo.TextInfo.ToTitleCase(prefix);
                displayName = $"{prefix}: {displayName}";
            }
            return displayName;
        }

        static string GetDescription(string description, int maxLength)
        {
            if (description.Length > maxLength)
            {
                return description.Substring(0, maxLength);
            }
            else
            {
                return description;
            }
        }

        static async Task<string> GetUniqueMailAlias(string name, string prefix, bool usePrefix)
        {
            var mailNickname = ReRemoveNonAlphaNumChars.Replace(name, "").ToLower();
            prefix = ReRemoveNonAlphaNumChars.Replace(prefix + "", "").ToLower();

            if (!string.IsNullOrWhiteSpace(prefix) && usePrefix)
            {
                mailNickname = $"{prefix}-{mailNickname}";
            }
            if (string.IsNullOrWhiteSpace(mailNickname))
            {
                mailNickname = new Random().Next(0, 9).ToString();
            }
            const int maxCharsInEmail = 40;
            if (mailNickname.Length > maxCharsInEmail)
            {
                mailNickname = mailNickname.Substring(0, maxCharsInEmail);
            }

            GraphServiceClient client = ConnectADAL.GetGraphClient();
            while (true)
            {
                IGraphServiceGroupsCollectionPage groupExist = await client.Groups.Request()
                    .Filter($"groupTypes/any(grp: grp eq 'Unified') and MailNickname eq '{mailNickname}'").Top(1)
                    .GetAsync();
                if (groupExist.Count > 0)
                {
                    mailNickname += new Random().Next(0, 9).ToString();
                }
                else
                {
                    break;
                }
            }
            return mailNickname;
        }

        public class CreateGroupRequest
        {
            [Required]
            [Display(Description = "Name of the group")]
            public string Name { get; set; }

            [Required]
            [Display(Description = "Description of the group")]
            public string Description { get; set; }

            [Display(Description = "Prefix for group display name / e-mail address")]
            public string Prefix { get; set; }

            [Required]
            [Display(Description = "Group responsible")]
            public string Responsible { get; set; }

            [Required]
            [Display(Description = "Should the group be public")]
            public bool Public { get; set; }

            [Required]
            [Display(Description = "If prefix is set, use for DisplayName")]
            public bool UsePrefixInDisplayName { get; set; }

            [Required]
            [Display(Description = "If prefix is set, use for EmailAlias")]
            public bool UsePrefixInMailAlias { get; set; }

            [Display(Description = "Classification")]
            public string Classification { get; set; }
        }

        public class CreateGroupResponse
        {
            [Display(Description = "Id of the Office 365 Group")]
            public string GroupId { get; set; }

            [Display(Description = "DisplayName of the Office 365 Group")]
            public string DisplayName { get; set; }

            [Display(Description = "Mail of the Office 365 Group")]
            public string Mail { get; set; }
        }
    }
}
