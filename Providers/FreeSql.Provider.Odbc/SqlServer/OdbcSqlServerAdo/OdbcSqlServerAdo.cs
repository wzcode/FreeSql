﻿using FreeSql.Internal;
using FreeSql.Internal.Model;
using SafeObjectPool;
using System;
using System.Collections;
using System.Data.Common;
using System.Data.Odbc;
using System.Linq;
using System.Text;
using System.Threading;

namespace FreeSql.Odbc.SqlServer
{
    class OdbcSqlServerAdo : FreeSql.Internal.CommonProvider.AdoProvider
    {
        public OdbcSqlServerAdo() : base(DataType.SqlServer) { }
        public OdbcSqlServerAdo(CommonUtils util, string masterConnectionString, string[] slaveConnectionStrings) : base(DataType.SqlServer)
        {
            base._util = util;
            if (!string.IsNullOrEmpty(masterConnectionString))
                MasterPool = new OdbcSqlServerConnectionPool("主库", masterConnectionString, null, null);
            if (slaveConnectionStrings != null)
            {
                foreach (var slaveConnectionString in slaveConnectionStrings)
                {
                    var slavePool = new OdbcSqlServerConnectionPool($"从库{SlavePools.Count + 1}", slaveConnectionString, () => Interlocked.Decrement(ref slaveUnavailables), () => Interlocked.Increment(ref slaveUnavailables));
                    SlavePools.Add(slavePool);
                }
            }
        }

        static DateTime dt1970 = new DateTime(1970, 1, 1);
        string[] ncharDbTypes = new[] { "NVARCHAR", "NCHAR", "NTEXT" };
        public override object AddslashesProcessParam(object param, Type mapType, ColumnInfo mapColumn)
        {
            if (param == null) return "NULL";
            if (mapType != null && mapType != param.GetType() && (param is IEnumerable == false || mapType.IsArrayOrList()))
                param = Utils.GetDataReaderValue(mapType, param);
            if (param is bool || param is bool?)
                return (bool)param ? 1 : 0;
            else if (param is string)
            {
                if (mapColumn != null && mapColumn.CsType.NullableTypeOrThis() == typeof(string) && ncharDbTypes.Any(a => mapColumn.Attribute.DbType.Contains(a)) == false)
                    return string.Concat("'", param.ToString().Replace("'", "''"), "'");
                return string.Concat("N'", param.ToString().Replace("'", "''"), "'");
            }
            else if (param is char)
                return string.Concat("'", param.ToString().Replace("'", "''"), "'");
            else if (param is Enum)
                return ((Enum)param).ToInt64();
            else if (decimal.TryParse(string.Concat(param), out var trydec))
                return param;
            else if (param is DateTime || param is DateTime?)
            {
                if (param.Equals(DateTime.MinValue) == true) param = new DateTime(1970, 1, 1);
                return string.Concat("'", ((DateTime)param).ToString("yyyy-MM-dd HH:mm:ss.fff"), "'");
            }
            else if (param is DateTimeOffset || param is DateTimeOffset?)
            {
                if (param.Equals(DateTimeOffset.MinValue) == true) param = new DateTimeOffset(new DateTime(1970, 1, 1), TimeSpan.Zero);
                return string.Concat("'", ((DateTimeOffset)param).ToString("yyyy-MM-dd HH:mm:ss.fff zzzz"), "'");
            }
            else if (param is TimeSpan || param is TimeSpan?)
                return ((TimeSpan)param).TotalSeconds;
            else if (param is IEnumerable)
            {
                var sb = new StringBuilder();
                var ie = param as IEnumerable;
                foreach (var z in ie) sb.Append(",").Append(AddslashesProcessParam(z, mapType, mapColumn));
                return sb.Length == 0 ? "(NULL)" : sb.Remove(0, 1).Insert(0, "(").Append(")").ToString();
            }
            return string.Concat("'", param.ToString().Replace("'", "''"), "'");
        }

        protected override DbCommand CreateCommand()
        {
            return new OdbcCommand();
        }

        protected override void ReturnConnection(ObjectPool<DbConnection> pool, Object<DbConnection> conn, Exception ex)
        {
            (pool as OdbcSqlServerConnectionPool).Return(conn, ex);
        }

        protected override DbParameter[] GetDbParamtersByObject(string sql, object obj) => _util.GetDbParamtersByObject(sql, obj);
    }
}