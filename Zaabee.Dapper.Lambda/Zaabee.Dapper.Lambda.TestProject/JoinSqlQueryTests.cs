﻿using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Xunit;
using Zaabee.Dapper.Lambda.TestProject.Entities;
using Zaabee.Dapper.Lambda.TestProject.Infrastructure;

namespace Zaabee.Dapper.Lambda.TestProject
{
    public class JoinSqlQueryTests : TestBase
    {
        /// <summary>
        /// Find all products for the category Beverages
        /// </summary>
        [Fact]
        public void FindByJoinedEntityValue()
        {
            const string categoryName = "Beverages";
            const int categoryId = 1;

            var query = new SqlLam<Product>()
                .Join<Category>((p, c) => p.CategoryId == c.CategoryId)
                .Where(c => c.CategoryName == categoryName);

            var results = Connection.Query<Product>(query.QueryString, query.QueryParameters).ToList();

            Assert.Equal(12, results.Count);
            Assert.True(results.All(p => p.CategoryId == categoryId));
        }

        /// <summary>
        /// Find products with category being one of Beverages, Condiments, or Seafood
        /// </summary>
        [Fact]
        public void FindByJoinedEntityListOfValues()
        {
            var categoryNames = new object[] { "Beverages", "Condiments", "Seafood" };
            var categoryIds = new[] { 1, 2, 8 };

            var query = new SqlLam<Product>()
                .Join<Category>((p, c) => p.CategoryId == c.CategoryId)
                .WhereIsIn(c => c.CategoryName, categoryNames);

            var results = Connection.Query<Product>(query.QueryString, query.QueryParameters).ToList();

            Assert.Equal(36, results.Count);
            Assert.True(results.All(p => categoryIds.Contains(p.CategoryId)));
        }

        /// <summary>
        /// Find products by getting the category Ids first using a sub query
        /// </summary>
        [Fact]
        public void FindByJoinedEntityWithSubQuery()
        {
            var categoryNames = new object[] { "Beverages", "Condiments", "Seafood" };
            var categoryIds = new[] { 1, 2, 8 };

            var subQuery = new SqlLam<Category>()
                .WhereIsIn(c => c.CategoryName, categoryNames)
                .Select(p => p.CategoryId);

            var query = new SqlLam<Product>()
                .Join<Category>((p, c) => p.CategoryId == c.CategoryId)
                .WhereIsIn(c => c.CategoryId, subQuery);

            var results = Connection.Query<Product>(query.QueryString, query.QueryParameters).ToList();

            Assert.Equal(36, results.Count);
            Assert.True(results.All(p => categoryIds.Contains(p.CategoryId)));
        }

        /// <summary>
        /// Get products sorted by categories
        /// </summary>
        [Fact]
        public void OrderEntitiesByJoinedEntityField()
        {
            var categoryQuery = new SqlLam<Category>();
            var categories = Connection.Query<Category>(categoryQuery.QueryString).ToDictionary(k => k.CategoryId);

            var query = new SqlLam<Product>()
                .Join<Category>((p, c) => p.CategoryId == c.CategoryId)
                .OrderBy(c => c.CategoryName);

            var results = Connection.Query<Product>(query.QueryString, query.QueryParameters).ToList();

            for (int i = 1; i < results.Count; ++i)
            {
                Assert.True(String.CompareOrdinal(
                    categories[results[i - 1].CategoryId].CategoryName, 
                    categories[results[i].CategoryId].CategoryName) 
                    <= 0);
            }
        }

        /// <summary>
        /// Get products sorted by categories
        /// </summary>
        [Fact]
        public void OrderEntitiesByJoinedEntityFieldDescending()
        {
            var categoryQuery = new SqlLam<Category>();
            var categories = Connection.Query<Category>(categoryQuery.QueryString).ToDictionary(k => k.CategoryId);

            var query = new SqlLam<Product>()
                .Join<Category>((p, c) => p.CategoryId == c.CategoryId)
                .OrderByDescending(c => c.CategoryName);

            var results = Connection.Query<Product>(query.QueryString, query.QueryParameters).ToList();

            for (int i = 1; i < results.Count; ++i)
            {
                Assert.True(String.CompareOrdinal(
                    categories[results[i - 1].CategoryId].CategoryName,
                    categories[results[i].CategoryId].CategoryName)
                    >= 0);
            }
        }

        /// <summary>
        /// Load products together with their category details (many to one relationship)
        /// The complex selection of the product properties is only required because of the identical column names in the Northwind database for the primary key column and the foreign key column
        /// </summary>
        [Fact]
        public void GetEntitiesWithManyToOneRelationship()
        {
            var query = new SqlLam<Product>()
                .Select(p => p.ProductId, p => p.ProductName, p => p.EnglishName, p => p.ReorderLevel, p => p.UnitPrice)
                .Join<Category>((p, c) => p.CategoryId == c.CategoryId)
                .Select(c => c);

            var result = Connection.Query<Product, Category, Product>(
                            query.QueryString,
                            (product, category) =>
                                {
                                    product.Category = category;
                                    product.CategoryId = category.CategoryId;
                                    return product;
                                },
                            splitOn: query.SplitColumns[0]).ToList();

            Assert.Equal(77, result.Count);
            foreach (var product in result)
            {
                Assert.NotNull(product.Category);
                Assert.NotNull(product.Category.CategoryName);
            }
        }

        /// <summary>
        /// Load categories together with all their products (one to many relationship)
        /// The complex selection of the product properties is only required because of the identical column names in the Northwind database for the primary key column and the foreign key column
        /// </summary>
        [Fact]
        public void GetEntitiesWithOneToManyRelationship()
        {
            var query = new SqlLam<Category>()
                .Select(c => c.CategoryName)
                .Join<Product>((c, p) => c.CategoryId == p.CategoryId)
                .Select(p => p.CategoryId, p => p.ProductId, p => p.ProductName, p => p.EnglishName, p => p.ReorderLevel, p => p.UnitPrice);

            var result = new Dictionary<int, Category>();
            Connection.Query<Category, Product, Category>(
                query.QueryString,
                (category, product) =>
                {
                    if(!result.ContainsKey(product.CategoryId))
                    {
                        category.CategoryId = product.CategoryId;
                        category.Products = new List<Product>();
                        result.Add(category.CategoryId,category);
                    }
                    product.Category = result[product.CategoryId];
                    result[product.CategoryId].Products.Add(product);
                    return category;
                },
                splitOn: query.SplitColumns[0]);

            Assert.Equal(8, result.Count);
            foreach (var category in result.Values)
            {
                Assert.True(category.Products.Count>0);
                foreach(var product in category.Products)
                {
                    Assert.Equal(category.CategoryId, product.CategoryId);
                }
            }
        }

        /// <summary>
        /// Load orders together with all their assigned products (many to many relationship via Order Details)
        /// </summary>
        [Fact]
        public void GetEntitiesWithManyToManyRelationship()
        {
            var query = new SqlLam<Order>()
                .Select(o => o)
                .Join<OrderDetails>((o, d) => o.OrderId == d.OrderId)
                .Join<Product>((d, p) => d.ProductId == p.ProductId)
                .Select(p => p);

            var result = new Dictionary<int, Order>();
            Connection.Query<Order, Product, Order>(
                query.QueryString,
                (order, product) =>
                {
                    if (!result.ContainsKey(order.OrderId))
                    {
                        order.Products = new List<Product>();
                        result.Add(order.OrderId, order);
                    }                   
                    result[order.OrderId].Products.Add(product);
                    return order;
                },
                splitOn: query.SplitColumns[1]);

            var controlQuery = new SqlLam<OrderDetails>();
            var controlSet =
                Connection.Query<OrderDetails>(controlQuery.QueryString).ToLookup(d => d.OrderId + "_" + d.ProductId);
            Assert.Equal(1078, result.Count);
            foreach (var order in result.Values)
            {
                Assert.True(order.Products.Count> 0);
                foreach (var product in order.Products)
                {
                    Assert.True(controlSet.Contains(order.OrderId + "_" + product.ProductId));
                }
            }
        }
    }
}
