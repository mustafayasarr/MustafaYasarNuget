﻿using MustafaYasarNuget.ElasticSearch.Abstract;
using MustafaYasarNuget.ElasticSearch.Exceptions;
using Nest;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MustafaYasarNuget.ElasticSearch.Concrete
{
    public class ElasticSearchService : IElasticSearchService
    {
        public IElasticClient ElasticSearchClient { get; set; }
        private readonly IElasticSearchConfiguration _elasticSearchConfiguration;
        public ElasticSearchService(IElasticSearchConfiguration elasticSearchConfiguration)
        {
            _elasticSearchConfiguration = elasticSearchConfiguration;
            ElasticSearchClient = GetClient();
        }
        private ElasticClient GetClient()
        {
            var str = _elasticSearchConfiguration.ConnectionString;
            string s = string.Empty;
            str.ToList().ForEach(x => s += x);

            var connectionString = new ConnectionSettings(new Uri(s))
                .DisablePing()
                .SniffOnStartup(false)
                .SniffOnConnectionFault(false);

            if (!string.IsNullOrEmpty(_elasticSearchConfiguration.AuthUserName) && !string.IsNullOrEmpty(_elasticSearchConfiguration.AuthPassWord))
                connectionString.BasicAuthentication(_elasticSearchConfiguration.AuthUserName, _elasticSearchConfiguration.AuthPassWord);

            return new ElasticClient(connectionString);
        }
        public virtual async Task AddOrUpdateAsync<T, TKey>(string indexName, T model) where T : ElasticEntity<TKey>
        {

            var exist = ElasticSearchClient.DocumentExists(DocumentPath<T>.Id(new Id(model)), dd => dd.Index(indexName));

            if (exist.Exists)
            {
                var result = await ElasticSearchClient.UpdateAsync(DocumentPath<T>.Id(new Id(model)),
                    ss => ss.Index(indexName).Doc(model).RetryOnConflict(3));

                if (result.ServerError == null) return;
                throw new ElasticSearchException($"Update Document failed at index{indexName} :" + result.ServerError.Error.Reason);
            }
            else
            {
                var result = await ElasticSearchClient.IndexAsync(model, ss => ss.Index(indexName));
                if (result.ServerError == null) return;
                throw new ElasticSearchException($"Insert Docuemnt failed at index {indexName} :" + result.ServerError.Error.Reason);
            }
        }
        public virtual async Task CreateIndexAsync<T, TKey>(string indexName) where T : ElasticEntity<TKey>
        {
            var exis = await ElasticSearchClient.Indices.ExistsAsync(indexName);

            if (exis.Exists)
                return;
            var newName = indexName + DateTime.Now.Ticks;
            var result = await ElasticSearchClient
                .Indices.CreateAsync(newName,
                     ss =>
                         ss.Index(newName)
                         .Settings(s => s.NumberOfShards(4).NumberOfReplicas(2).Setting("max_result_window", int.MaxValue).Analysis(x => x.TokenFilters(tkf => tkf.AsciiFolding("my_ascii_folding", af => af.PreserveOriginal(true)))
                            .Analyzers(aa => aa.Custom("turkish_analyzer", ca => ca.Filters("lowercase", "my_ascii_folding")
                               .Tokenizer("standard")
                            )))
                         ).Map<T>(a => a.AutoMap()
                         .Properties(p => p.Text(t => t.Name(n => n.SearchingArea)
                         .Analyzer("turkish_analyzer"))))

             );
            if (result.Acknowledged)
            {
                await ElasticSearchClient.Indices.BulkAliasAsync(al => al.Add(add => add.Index(newName).Alias(indexName)));
                return;
            }
            throw new ElasticSearchException($"Create Index {indexName} failed : :" + result.ServerError.Error.Reason);
        }
        public virtual async Task DeleteAsync<T, TKey>(string indexName, string typeName, T model) where T : ElasticEntity<TKey>
        {

            var response = await ElasticSearchClient.DeleteAsync(new DeleteRequest(indexName, new Id(model)));
            if (response.ServerError == null)
                return;
            throw new ElasticSearchException($"Delete Document at index {indexName} :{response.ServerError.Error.Reason}");

        }
        public virtual async Task DeleteIndexAsync(string indexName)
        {
            var response = await ElasticSearchClient.Indices.DeleteAsync(indexName);
            if (response.Acknowledged) return;
            throw new ElasticSearchException($"Delete index {indexName} failed :{response.ServerError.Error.Reason}");
        }
        public virtual async Task ReIndex<T, TKey>(string indexName) where T : ElasticEntity<TKey>
        {
            await DeleteIndexAsync(indexName);
            await CreateIndexAsync<T, TKey>(indexName);
        }
        public virtual async Task<ISearchResponse<T>> SearchAsync<T, TKey>(string indexName, SearchDescriptor<T> query, int skip, int size, string[] includeFields = null, string preTags = "<strong style=\"color:red;\">", string postTags = "</strong>", bool disableHigh = false, params string[] highField) where T : ElasticEntity<TKey>
        {
            query.Index(indexName);
            var highdes = new HighlightDescriptor<T>();
            if (disableHigh)
            {
                preTags = "";
                postTags = "";
            }
            highdes.PreTags(preTags).PostTags(postTags);
            var ishigh = highField != null && highField.Length > 0;
            var hfs = new List<Func<HighlightFieldDescriptor<T>, IHighlightField>>();

            //Pagination
            query.Skip(skip).Take(size);
            //Keyword highlighting
            if (ishigh)
            {
                foreach (var s in highField)
                {
                    hfs.Add(f => f.Field(s));
                }
            }
            highdes.Fields(hfs.ToArray());
            query.Highlight(h => highdes);
            if (includeFields != null)
                query.Source(ss => ss.Includes(ff => ff.Fields(includeFields.ToArray())));

            var data = JsonConvert.SerializeObject(query);
            var response = await ElasticSearchClient.SearchAsync<T>(query);

            return response;
        }
        public virtual async Task<ISearchResponse<T>> SimpleSearchAsync<T, TKey>(string indexName, SearchDescriptor<T> query) where T : ElasticEntity<TKey>
        {
            query.Index(indexName);
            var response = await ElasticSearchClient.SearchAsync<T>(query);
            return response;
        }

        public virtual async Task BulkAddorUpdateAsync<T, TKey>(string indexName, List<T> list, int bulkNum = 1000) where T : ElasticEntity<TKey>
        {
            if (list.Count <= bulkNum)
                await BulkAddOrUpdate<T, TKey>(indexName, list);
            else
            {
                var total = (int)Math.Ceiling(list.Count * 1.0f / bulkNum);
                var tasks = new List<Task>();
                for (var i = 0; i < total; i++)
                {
                    var i1 = i;
                    tasks.Add(await Task.Factory.StartNew(async () => await BulkAddOrUpdate<T, TKey>(indexName, list.Skip(i1 * bulkNum).Take(bulkNum).ToList()))); ;
                }
                await Task.WhenAll(tasks.ToArray());
            }
        }
        public virtual async Task CreateIndexSuggestAsync<T, TKey>(string indexName) where T : ElasticEntity<TKey>
        {
            var exis = await ElasticSearchClient.Indices.ExistsAsync(indexName);

            if (exis.Exists)
                return;
            var newName = indexName + DateTime.Now.Ticks;

            var createIndexDescriptor = new CreateIndexDescriptor(newName)
                  .Settings(o => o.NumberOfShards(1).NumberOfReplicas(1).Setting("max_result_window", int.MaxValue))

                           .Map<T>(m => m
                                 .AutoMap()
                                 .Properties(ps => ps
                                     .Completion(c => c
                                         .Name(p => p.Suggest))));

            var result = await ElasticSearchClient
                 .Indices.CreateAsync(createIndexDescriptor);

            if (result.Acknowledged)
            {
                await ElasticSearchClient.Indices.BulkAliasAsync(al => al.Add(add => add.Index(newName).Alias(indexName)));
                return;
            }
            throw new ElasticSearchException($"Create Index {indexName} failed : :" + result.ServerError.Error.Reason);
        }

        public virtual async Task CrateIndexAsync(string indexName)
        {
            var exist = await ElasticSearchClient.Indices.ExistsAsync(indexName);
            if (exist.Exists)
                return;
            var newName = indexName + DateTime.Now.Ticks;
            var result = await ElasticSearchClient
                         .Indices.CreateAsync(newName,
                             ss =>
                                 ss.Index(newName)
                                     .Settings(
                                         o => o.NumberOfShards(4).NumberOfReplicas(2).Setting("max_result_window", int.MaxValue)));
            if (result.Acknowledged)
            {
                await ElasticSearchClient.Indices.BulkAliasAsync(al => al.Add(add => add.Index(newName).Alias(indexName)));
                return;
            }
            throw new ElasticSearchException($"Create Index {indexName} failed :" + result.ServerError.Error.Reason);
        }

        public virtual async Task ReBuild<T, TKey>(string indexName) where T : ElasticEntity<TKey>
        {
            var result = await ElasticSearchClient.Indices.GetAliasAsync(indexName);
            var oldName = result.Indices.Keys.FirstOrDefault();
            if (oldName == null)
            {
                throw new ElasticSearchException($"not found index {indexName}");
            }
            var newIndex = indexName + DateTime.Now.Ticks;
            var createResult = await ElasticSearchClient.Indices.CreateAsync(newIndex,
                           c =>
                               c.Index(newIndex)
                                   .Map<T>(m => m.AutoMap()));
            if (!createResult.Acknowledged)
            {
                throw new ElasticSearchException($"reBuild create newIndex {indexName} failed :{result.ServerError.Error.Reason}");
            }
            //Rebuild index data
            var reResult = await ElasticSearchClient.ReindexOnServerAsync(descriptor => descriptor.Source(source => source.Index(indexName))
                .Destination(dest => dest.Index(newIndex)));
            if (reResult.ServerError != null)
            {
                throw new ElasticSearchException($"reBuild {indexName} datas failed :{reResult.ServerError.Error.Reason}");
            }
            //Delete old index
            var alReuslt = await ElasticSearchClient.Indices.BulkAliasAsync(al => al.Remove(rem => rem.Index(oldName.Name).Alias(indexName)).Add(add => add.Index(newIndex).Alias(indexName)));
            if (!alReuslt.Acknowledged)
            {
                throw new ElasticSearchException($"reBuild set Alias {indexName}  failed :{alReuslt.ServerError.Error.Reason}");
            }
            var delResult = await ElasticSearchClient.Indices.DeleteAsync(oldName);
            throw new ElasticSearchException($"reBuild delete old Index {oldName.Name} failed :" + delResult.ServerError.Error.Reason);
        }

        public virtual async Task CreateIndexCustomSuggestAsync<T, TKey>(string indexName) where T : ElasticEntity<TKey>
        {
            var exist = await ElasticSearchClient.Indices.ExistsAsync(indexName);
            if (exist.Exists)
                return;
            var newName = indexName + DateTime.Now.Ticks;

            var createIndexDescriptor = new CreateIndexDescriptor(newName)
                     .Settings(o => o.NumberOfShards(1).NumberOfReplicas(1).Setting("max_result_window", int.MaxValue))
                          .Map<T>(m => m
                                    .AutoMap()
                                    .Properties(ps => ps
                                        .Completion(c => c
                                           .Contexts(ctx => ctx.Category(csg => csg.Name("userId").Path("u")))
                                             .Name(d => d.Suggest)
                                           )));
            var result = await ElasticSearchClient
               .Indices.CreateAsync(createIndexDescriptor);
            if (result.Acknowledged)
            {
                await ElasticSearchClient.Indices.BulkAliasAsync(al => al.Add(add => add.Index(newName).Alias(indexName)));
                return;
            }
            throw new ElasticSearchException($"Create Index {indexName} failed : :" + result.ServerError.Error.Reason);


        }

        public virtual async Task BulkDeleteAsync<T, TKey>(string indexName, List<T> list, int bulkNum = 1000) where T : ElasticEntity<TKey>
        {
            if (list.Count <= bulkNum)
                await BulkDelete<T, TKey>(indexName, list);
            else
            {
                var total = (int)Math.Ceiling(list.Count * 1.0f / bulkNum);
                var tasks = new List<Task>();
                for (var i = 0; i < total; i++)
                {
                    var i1 = i;
                    tasks.Add(await Task.Factory.StartNew(async () => await BulkDelete<T, TKey>(indexName, list.Skip(i1 * bulkNum).Take(bulkNum).ToList())));
                }
                await Task.WhenAll(tasks);

            }
        }

        #region private Methods
        private async Task BulkAddOrUpdate<T, TKey>(string indexName, List<T> list) where T : ElasticEntity<TKey>
        {
            var bulk = new BulkRequest(indexName)
            {
                Operations = new List<IBulkOperation>()
            };
            foreach (var item in list)
            {
                bulk.Operations.Add(new BulkIndexOperation<T>(item));
            }
            var response = await ElasticSearchClient.BulkAsync(bulk);
            if (response.Errors)
                throw new ElasticSearchException($"Bulk InsertOrUpdate Docuemnt failed at index {indexName} :{response.ServerError.Error.Reason}");
        }
        private async Task BulkDelete<T, TKey>(string indexName, List<T> list) where T : ElasticEntity<TKey>
        {
            var bulk = new BulkRequest(indexName)
            {
                Operations = new List<IBulkOperation>()
            };
            foreach (var item in list)
            {
                bulk.Operations.Add(new BulkDeleteOperation<T>(new Id(item)));
            }
            var response = await ElasticSearchClient.BulkAsync(bulk);
            if (response.Errors)
                throw new ElasticSearchException($"Bulk Delete Docuemnt at index {indexName} :{response.ServerError.Error.Reason}");
        }
        #endregion
    }
}
