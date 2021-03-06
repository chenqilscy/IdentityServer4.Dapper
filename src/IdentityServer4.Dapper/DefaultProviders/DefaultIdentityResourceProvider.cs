﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Linq;
using Dapper;
using IdentityServer4.Dapper.Interfaces;
using IdentityServer4.Dapper.Mappers;
using IdentityServer4.Dapper.Options;
using IdentityServer4.Models;
using Microsoft.Extensions.Logging;

namespace IdentityServer4.Dapper.DefaultProviders
{
    public class DefaultIdentityResourceProvider : IIdentityResourceProvider
    {
        private DBProviderOptions _options;
        private readonly ILogger<DefaultIdentityResourceProvider> _logger;

        public DefaultIdentityResourceProvider(DBProviderOptions dBProviderOptions, ILogger<DefaultIdentityResourceProvider> logger)
        {
            this._options = dBProviderOptions ?? throw new ArgumentNullException(nameof(dBProviderOptions));
            this._logger = logger;
        }

        public IEnumerable<IdentityResource> FindIdentityResourcesAll()
        {
            using (var connection = _options.DbProviderFactory.CreateConnection())
            {
                connection.ConnectionString = _options.ConnectionString;
                var claims = connection.Query<Entities.IdentityClaim, Entities.IdentityResource, Entities.IdentityClaim>("select * from IdentityClaims claim inner join IdentityResources ic on claim.IdentityResourceId = ic.id", (claim, indetity) => { claim.IdentityResource = indetity; return claim; }, commandTimeout: _options.CommandTimeOut, commandType: CommandType.Text);
                var identities = claims?.Select(c => c.IdentityResource).Distinct();
                if (identities != null)
                {
                    foreach (var item in identities)
                    {
                        item.UserClaims = claims?.Where(c => c.IdentityResource.Id == item.Id).AsList();
                    }
                }
                return identities.Select(c => c.ToModel());
            }
        }

        public IdentityResource FindIdentityResourcesByName(string name)
        {
            return GetByName(name).ToModel();
        }

        public Entities.IdentityResource GetByName(string name)
        {
            using (var connection = _options.DbProviderFactory.CreateConnection())
            {
                connection.ConnectionString = _options.ConnectionString;
                var claims = connection.Query<Entities.IdentityClaim, Entities.IdentityResource, Entities.IdentityClaim>($"select * from IdentityClaims claim inner join IdentityResources ic on claim.IdentityResourceId = ic.id where ic.Name = @Name", (claim, indetity) => { claim.IdentityResource = indetity; return claim; }, new { Name = name }, commandTimeout: _options.CommandTimeOut, commandType: CommandType.Text);
                var identities = claims?.Select(c => c.IdentityResource).FirstOrDefault();
                if (identities != null)
                {
                    identities.UserClaims = claims?.Where(c => c.IdentityResource.Id == identities.Id).AsList();
                }
                return identities;
            }
        }

        public IEnumerable<Entities.IdentityClaim> GetClaimsByName(string identityName)
        {
            using (var connection = _options.DbProviderFactory.CreateConnection())
            {
                connection.ConnectionString = _options.ConnectionString;
                return connection.Query<Entities.IdentityClaim>("select claim.* from IdentityClaims claim inner join IdentityResources ic on claim.IdentityResourceId = ic.id where ic.Name = @Name",  new { Name = identityName }, commandTimeout: _options.CommandTimeOut, commandType: CommandType.Text);
            }
        }

        public IEnumerable<IdentityResource> FindIdentityResourcesByScope(IEnumerable<string> scopeNames)
        {
            if (scopeNames == null || scopeNames.Count() == 0)
            {
                return null;
            }

            if (scopeNames.Count() > 20)
            {
                var lstall = FindIdentityResourcesAll();
                return lstall.Where(c => scopeNames.Contains(c.Name));
            }
            else
            {
                using (var connection = _options.DbProviderFactory.CreateConnection())
                {
                    connection.ConnectionString = _options.ConnectionString;

                    DynamicParameters parameters = new DynamicParameters();
                    StringBuilder conditions = new StringBuilder();
                    int index = 1;
                    foreach (var item in scopeNames)
                    {
                        if (string.IsNullOrWhiteSpace(item))
                        {
                            continue;
                        }
                        conditions.Append($"@Scope{index},");
                        parameters.Add($"@Scope{index}", item);
                    }

                    string sql = $"select * from IdentityClaims claim inner join IdentityResources ic on claim.IdentityResourceId = ic.id where ic.Name in ({conditions.ToString().TrimEnd(',')})";

                    var task = connection.QueryAsync<Entities.IdentityClaim, Entities.IdentityResource, Entities.IdentityClaim>(sql, (claim, indetity) => { claim.IdentityResource = indetity; return claim; }, parameters, commandTimeout: _options.CommandTimeOut, commandType: CommandType.Text);
                    while (!task.IsCompleted)
                    {
                        System.Threading.Thread.Sleep(10);
                    }
                    var claims = task.Result;
                    var identities = claims?.Select(c => c.IdentityResource).Distinct();
                    if (identities != null)
                    {
                        foreach (var item in identities)
                        {
                            item.UserClaims = claims?.Where(c => c.IdentityResource.Id == item.Id).AsList();
                        }
                    }
                    return identities.Select(c => c.ToModel());
                }
            }
        }

        public void Add(IdentityResource identityResource)
        {
            var dbidentityResource = FindIdentityResourcesByName(identityResource.Name);
            if (dbidentityResource != null)
            {
                throw new InvalidOperationException($"you can not add an existed identityResource,Name={dbidentityResource.Name}.");
            }

            var entity = identityResource.ToEntity();
            using (var con = _options.DbProviderFactory.CreateConnection())
            {
                con.ConnectionString = _options.ConnectionString;
                con.Open();
                using (var t = con.BeginTransaction())
                {
                    try
                    {
                        string left = _options.ColumnProtect["left"];
                        string right = _options.ColumnProtect["right"];

                        var identityResourceid = con.ExecuteScalar<int>($"insert into IdentityResources ({left}Description{right},DisplayName,Emphasize,Enabled,{left}Name{right},Required,ShowInDiscoveryDocument) values (@Description,@DisplayName,@Emphasize,@Enabled,@Name,@Required,@ShowInDiscoveryDocument);{_options.GetLastInsertID}", new
                        {
                            entity.Description,
                            entity.DisplayName,
                            entity.Emphasize,
                            entity.Enabled,
                            entity.Name,
                            entity.Required,
                            entity.ShowInDiscoveryDocument
                        }, commandTimeout: _options.CommandTimeOut, commandType: CommandType.Text, transaction: t);


                        var ret = 0;

                        if (entity.UserClaims != null && entity.UserClaims.Count() > 0)
                        {
                            foreach (var item in entity.UserClaims)
                            {
                                ret = con.Execute($"insert into IdentityClaims ({left}IdentityResourceId{right},{left}Type{right}) values (@identityResourceid,@Type)", new
                                {
                                    identityResourceid,
                                    item.Type
                                }, commandTimeout: _options.CommandTimeOut, commandType: CommandType.Text, transaction: t);
                                if (ret != 1)
                                {
                                    throw new Exception($"execute insert error,return values is {ret}");
                                }
                            }
                        }

                        t.Commit();
                    }
                    catch (Exception ex)
                    {
                        t.Rollback();
                        throw ex;
                    }
                }
            }
        }


        public void Remove(string name)
        {
            var entity = GetByName(name);
            if (entity == null)
            {
                return;
            }
            using (var con = _options.DbProviderFactory.CreateConnection())
            {
                con.ConnectionString = _options.ConnectionString;
                con.Open();
                using (var t = con.BeginTransaction())
                {
                    try
                    {
                        var ret = con.Execute($"delete from IdentityResources where id=@id", new { entity.Id }, commandTimeout: _options.CommandTimeOut, commandType: CommandType.Text, transaction: t);
                        ret = con.Execute("delete from IdentityClaims where IdentityResourceId=@IdentityResourceId;", new
                        {
                            IdentityResourceId = entity.Id
                        }, commandTimeout: _options.CommandTimeOut, commandType: CommandType.Text, transaction: t);
                        t.Commit();
                    }
                    catch (Exception ex)
                    {
                        t.Rollback();
                        throw ex;
                    }
                }
            }
        }

        public IEnumerable<IdentityResource> Search(string keywords, int pageIndex, int pageSize, out int totalCount)
        {
            using (var connection = _options.DbProviderFactory.CreateConnection())
            {
                connection.ConnectionString = _options.ConnectionString;

                DynamicParameters pairs = new DynamicParameters();
                pairs.Add("keywords", "%" + keywords + "%");

                var countsql = "select count(1) from IdentityResources where Name like @keywords or DisplayName like @keywords or Description like @keywords";
                totalCount = connection.ExecuteScalar<int>(countsql, pairs, commandType: CommandType.Text);

                if (totalCount == 0)
                {
                    return null;
                }

                var clients = connection.Query<Entities.IdentityResource>(_options.GetPageQuerySQL("select * from IdentityResources where  Name like @keywords or DisplayName like @keywords or Description like @keywords", pageIndex, pageSize, totalCount, "", pairs), pairs, commandTimeout: _options.CommandTimeOut, commandType: CommandType.Text);
                if (clients != null)
                {
                    return clients.Select(c => c.ToModel());
                }
                return null;
            }
        }
    }
}
