using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Description;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Cumulus.Monads.Helpers;
using Microsoft.Graph;

namespace Cumulus.Monads.Graph
{
    public static class RemoveGroupMembers
    {
        public static string ToDebugString<TKey, TValue>(this IDictionary<TKey, TValue> dictionary)
        {
            return "{" + string.Join(",", dictionary.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}";
        }

        [FunctionName("RemoveGroupMembers")]
        [ResponseType(typeof(RemoveGroupMembersResponse))]
        [Display(Name = "Remove group members", Description = "")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "post")]RemoveGroupMembersRequest request, TraceWriter log)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.GroupId))
                {
                    throw new ArgumentException("Parameter cannot be null", "GroupId");
                }
                GraphServiceClient client = ConnectADAL.GetGraphClient(GraphEndpoint.v1);
                var group = client.Groups[request.GroupId];
                var memberOf = new List<IUserMemberOfCollectionWithReferencesPage>();
                var members = await group.Members.Request().GetAsync();
                for (int i = 0; i < members.Count; i++)
                {
                    var member = members[i];
                    log.Info($"Removing user {member.Id} from group {request.GroupId}");
                    await group.Members[member.Id].Reference.Request().DeleteAsync();
                }
                for (int i = 0; i < members.Count; i++)
                {
                    var member = members[i];
                    log.Info(ToDebugString(member.AdditionalData));
                    log.Info($"Retrieving memberOf for user {member.Id}");
                    var memberOfPage = await client.Users[member.Id].MemberOf.Request().GetAsync();
                    memberOf.Add(memberOfPage);
                }
                var removeGroupMembersResponse = new RemoveGroupMembersResponse {
                    RemovedMembers = members,
                    MemberOf = memberOf,
                };
                return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ObjectContent<RemoveGroupMembersResponse>(removeGroupMembersResponse, new JsonMediaTypeFormatter())
                });
            }
            catch (Exception e)
            {
                log.Error(e.Message);
                return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new ObjectContent<string>(e.Message, new JsonMediaTypeFormatter())
                });
            }
        }

        public class RemoveGroupMembersRequest
        {
            [Required]
            [Display(Description = "Id of the Office 365 Group")]
            public string GroupId { get; set; }
        }

        public class RemoveGroupMembersResponse
        {
            [Display(Description = "True/false if members was removed")]
            public IGroupMembersCollectionWithReferencesPage RemovedMembers { get; set; }
            public List<IUserMemberOfCollectionWithReferencesPage> MemberOf { get; set; }
        }
    }
}
