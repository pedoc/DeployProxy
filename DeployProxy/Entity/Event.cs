using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// ReSharper disable InconsistentNaming
//see http://gitlab.pedoc.cn/help/user/project/integrations/webhooks

namespace DeployProxy.Entity
{
    public class Event
    {
        public string object_kind { get; set; }
        public string before { get; set; }
        public string after { get; set; }
        public string @ref { get; set; }
        public string checkout_sha { get; set; }
        public int user_id { get; set; }
        public string user_name { get; set; }
        public string user_username { get; set; }
        public string user_email { get; set; }
        public string user_avatar { get; set; }
        public int project_id { get; set; }
        public Project project { get; set; }
        public Repository repository { get; set; }
        public List<Commit> commits { get; set; }
        public int total_commits_count { get; set; }
    }
}
