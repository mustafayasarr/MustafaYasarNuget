﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MustafaYasarNuget.ElasticSearch.Abstract
{
    public interface IElasticSearchConfiguration
    {
        string ConnectionString { get; }
        string AuthUserName { get; }
        string AuthPassWord { get; }
    }
}
