﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Raven.Server.Documents.ETL.Providers.SQL.RelationalWriters;

namespace Raven.Server.Documents.ETL.Providers.SQL.Simulation
{
    public class TableQuerySummary
    {
        public string TableName { get; set; }
        public CommandData[] Commands { get; set; }


        public class CommandData
        {
            public string CommandText { get; set; }
            public KeyValuePair<string, object>[] Params { get; set; }
        }

        public static TableQuerySummary GenerateSummaryFromCommands(string tableName, IEnumerable<DbCommand> commands)
        {
            var tableQuerySummary = new TableQuerySummary();
            tableQuerySummary.TableName = tableName;

            var commandData = new List<CommandData>();

            foreach (var c in commands)
            {
                var @params = new List<KeyValuePair<string, object>>();

                foreach (var param in c.Parameters.Cast<DbParameter>())
                {
                    var paramterValue = GetParameterValue(param);

                    @params.Add(new KeyValuePair<string, object>(param.ParameterName, paramterValue));
                }

                commandData.Add(new CommandData
                {
                    CommandText = c.CommandText,
                    Params = @params.ToArray()
                });
            }

            tableQuerySummary.Commands = commandData.ToArray();

            return tableQuerySummary;
        }

        public static object GetParameterValue(DbParameter param)
        {
            var paramterValue = param.Value;

            if (paramterValue == DBNull.Value)
            {
                paramterValue = "NULL";
            }
            else if (param.DbType == DbType.AnsiString || param.DbType == DbType.String)
            {
                paramterValue = $"'{RelationalDatabaseWriter.SanitizeSqlValue(paramterValue.ToString())}'";
            }

            return paramterValue;
        }
    }
}
