using Microsoft.Extensions.Configuration;
using MustafaYasarNuget.ElasticSearch.Abstract;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MustafaYasarNuget.ElasticSearch.Concrete
{
    public class ElasticSearchConfiguration : IElasticSearchConfiguration
    {
        public IConfiguration Configuration { get; }
        public ElasticSearchConfiguration(IConfiguration configuration,string connectionString,string userName,string passWord)
        {
            Configuration = configuration;
            ConnectionString = connectionString;
            AuthUserName = userName;
            AuthPassWord = passWord;
        }
        public string ConnectionString { get; }
        public string AuthUserName { get; }
        public string AuthPassWord { get; }
    }
}
