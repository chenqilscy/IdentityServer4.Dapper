﻿using IdentityServer4.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IdentityServer4.Dapper.Interfaces
{
    public interface IIdentityResourceProvider
    {
        IEnumerable<IdentityResource> FindIdentityResourcesByScope(IEnumerable<string> scopeNames);
        IEnumerable<IdentityResource> FindIdentityResourcesAll();
        void Add(IdentityResource identityResource);
        IdentityResource FindIdentityResourcesByName(string name);

        void Remove(string name);
        IEnumerable<IdentityResource> Search(string keywords, int pageIndex, int pageSize, out int totalCount);

    }
}
