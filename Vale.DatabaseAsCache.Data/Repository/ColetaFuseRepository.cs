using Dapper;
using log4net;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Transactions;
using Vale.DatabaseAsCache.Data.TableModels;

namespace Vale.DatabaseAsCache.Data.Repository
{
    public interface IColetaFuseRepository
    {
        bool IsConnectionOpen();
        ColetaFuseData SelectMostRecentWithStatusPending();
        int Insert(ColetaFuseData data);
        bool UpdateStatusToDone(ColetaFuseData data);
    }

    public class ColetaFuseRepository : IColetaFuseRepository
    {
        private static readonly ILog _log = LogManager.GetLogger("log");
        private readonly string _tableName = "ColetaFuse";
        private readonly IDbConnection _connection;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="connectionString">Database connection string.</param>
        public ColetaFuseRepository(string connectionString)
        {
            _connection = new SqlConnection(connectionString);
        }

        /// <summary>
        /// Valida a capacidade de conexão com o banco.
        /// </summary>
        /// <returns>Se a conexão foi aberta.</returns>
        public bool IsConnectionOpen()
        {
            bool isOpen;
            try
            {
                using (var transaction = new TransactionScope())
                {
                    _connection.Open();
                    isOpen = _connection.State.Equals(ConnectionState.Open);
                    _connection.Close();
                    transaction.Complete();
                }
            }
            catch (SqlException)
            {
                isOpen = false;
            }
            catch (TransactionAbortedException ex)
            {
                _log.ErrorFormat("Checagem de conexão ao banco abortada durante transação: {0}", ex.ToString().Replace(Environment.NewLine, string.Empty));
                isOpen = false;
            }
            return isOpen;
        }

        /// <summary>
        /// SelectMostRecentWithStatusPending data based on filter sent.
        /// </summary>
        /// <param name="data">Use as filter</param>
        /// <returns>List of data searched.</returns>
        public ColetaFuseData SelectMostRecentWithStatusPending()
        {
            ColetaFuseData response = null;
            try
            {
                using (var transaction = new TransactionScope())
                {
                    string sql = $@"
SELECT [PIER_CODE]
    ,[BOARDING_CODE]
    ,[INCREMENT_NUMBER]
    ,[PRODUCT_CODE]
    ,[CLIENT_CODE]
    ,[STACKER_NAME]
    ,[ESTIMATED_WEIGHT]
    ,[PORAO1_ID]
    ,[PORAO1_PESO1]
    ,[PORAO1_PESO2]
    ,[PORAO2_ID]
    ,[PORAO2_PESO1]
    ,[PORAO2_PESO2]
    ,[PORAO3_ID]
    ,[PORAO3_PESO1]
    ,[PORAO3_PESO2]
    ,[ORDER_NUMBER]
    ,[BOARDING_LINE]
    ,[INCREMENT_DATETIME]
    ,[PARTIAL_SAMPLE]
    ,[SUBPARTIAL_SAMPLE]
    ,[SUBSUBPARTIAL_SAMPLE]
    ,[WEIGHTATCUT]
    ,[STATUS_TYPE]
FROM [dbo].[{_tableName}]
WHERE [STATUS_TYPE] = 'PENDING'
ORDER BY [INCREMENT_DATETIME] DESC";
                    response = _connection.QueryFirstOrDefault<ColetaFuseData>(sql);
                    transaction.Complete();
                }
            }
            catch (SqlException ex)
            {
                _log.ErrorFormat("Erro ao executar seleção: {0}", ex.ToString().Replace(Environment.NewLine, string.Empty));
            }
            catch (TransactionAbortedException ex)
            {
                _log.ErrorFormat("Seleção no banco abortada durante transação: {0}", ex.ToString().Replace(Environment.NewLine, string.Empty));
            }

            return response;
        }

        /// <summary>
        /// Insert new line into table
        /// </summary>
        /// <param name="data">Objeto com dados a serem inseridos.</param>
        /// <returns>Number of lines inserted. Probably one. Negative value means an error.</returns>
        public int Insert(ColetaFuseData data)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            int numRowsInserted = -1;
            try
            {

                using (var transaction = new TransactionScope())
                {
                    var sql = $@"
INSERT INTO [dbo].[{_tableName}]
    ([PRODUCT_CODE]
    ,[INCREMENT_NUMBER]
    ,[PIER_CODE]
    {(data.CLIENT_CODE is null ? string.Empty : ",[CLIENT_CODE]")}
    {(data.BOARDING_CODE is null ? string.Empty : ",[BOARDING_CODE]")}
    {(data.STACKER_NAME is null ? string.Empty : ",[STACKER_NAME]")}
    {(data.ESTIMATED_WEIGHT.HasValue ? ",[ESTIMATED_WEIGHT]" : string.Empty)}
    {(data.PORAO1_ID.HasValue ? ",[PORAO1_ID]" : string.Empty)}
    {(data.PORAO1_PESO1.HasValue ? ",[PORAO1_PESO1]" : string.Empty)}
    {(data.PORAO1_PESO2.HasValue ? ",[PORAO1_PESO2]" : string.Empty)}
    {(data.PORAO2_ID.HasValue ? ",[PORAO2_ID]" : string.Empty)}
    {(data.PORAO2_PESO1.HasValue ? ",[PORAO2_PESO1]" : string.Empty)}
    {(data.PORAO2_PESO2.HasValue ? ",[PORAO2_PESO2]" : string.Empty)}
    {(data.PORAO3_ID.HasValue ? ",[PORAO3_ID]" : string.Empty)}
    {(data.PORAO3_PESO1.HasValue ? ",[PORAO3_PESO1]" : string.Empty)}
    {(data.PORAO3_PESO2.HasValue ? ",[PORAO3_PESO2]" : string.Empty)}
    {(data.ORDER_NUMBER.HasValue ? ",[ORDER_NUMBER]" : string.Empty)}
    {(data.BOARDING_LINE.HasValue ? ",[BOARDING_LINE]" : string.Empty)}
    ,[INCREMENT_DATETIME]
    {(data.PARTIAL_SAMPLE is null ? string.Empty : ",[PARTIAL_SAMPLE]")}
    {(data.SUBPARTIAL_SAMPLE is null ? string.Empty : ",[SUBPARTIAL_SAMPLE]")}
    {(data.SUBSUBPARTIAL_SAMPLE is null ? string.Empty : ",[SUBSUBPARTIAL_SAMPLE]")}
    {(data.WEIGHTATCUT.HasValue ? ",[WEIGHTATCUT]" : string.Empty)}
    ,[STATUS_TYPE]
    ,[ERRO_LEITURA])
VALUES
    ('{data.PRODUCT_CODE}'
    ,{data.INCREMENT_NUMBER}
    ,'{data.PIER_CODE}'
    {(data.CLIENT_CODE is null ? string.Empty : $",'{data.CLIENT_CODE}'")}
    {(data.BOARDING_CODE is null ? string.Empty : $",'{data.BOARDING_CODE}'")}
    {(data.STACKER_NAME is null ? string.Empty : $",'{data.STACKER_NAME}'")}
    {(data.ESTIMATED_WEIGHT.HasValue ? $",{data.ESTIMATED_WEIGHT}" : string.Empty)}
    {(data.PORAO1_ID.HasValue ? $",'{data.PORAO1_ID}'" : string.Empty)}
    {(data.PORAO1_PESO1.HasValue ? $",'{data.PORAO1_PESO1}'" : string.Empty)}
    {(data.PORAO1_PESO2.HasValue ? $",'{data.PORAO1_PESO2}'" : string.Empty)}
    {(data.PORAO2_ID.HasValue ? $",'{data.PORAO2_ID}'" : string.Empty)}
    {(data.PORAO2_PESO1.HasValue ? $",'{data.PORAO2_PESO1}'" : string.Empty)}
    {(data.PORAO2_PESO2.HasValue ? $",'{data.PORAO2_PESO2}'" : string.Empty)}
    {(data.PORAO3_ID.HasValue ? $",'{data.PORAO3_ID}'" : string.Empty)}
    {(data.PORAO3_PESO1.HasValue ? $",'{data.PORAO3_PESO1}'" : string.Empty)}
    {(data.PORAO3_PESO2.HasValue ? $",'{data.PORAO3_PESO2}'" : string.Empty)}
    {(data.ORDER_NUMBER.HasValue ? $",'{data.ORDER_NUMBER}'" : string.Empty)}
    {(data.BOARDING_LINE.HasValue ? $",{data.BOARDING_LINE}" : string.Empty)}
    ,CAST(N'{data.INCREMENT_DATETIME:yyyy-MM-dd HH:mm:ss.fff}' AS DateTime)
    {(data.PARTIAL_SAMPLE is null ? string.Empty : $",'{data.PARTIAL_SAMPLE}'")}
    {(data.SUBPARTIAL_SAMPLE is null ? string.Empty : $",'{data.SUBPARTIAL_SAMPLE}'")}
    {(data.SUBSUBPARTIAL_SAMPLE is null ? string.Empty : $",'{data.SUBSUBPARTIAL_SAMPLE}'")}
    {(data.WEIGHTATCUT.HasValue ? $",{data.WEIGHTATCUT}" : string.Empty)}
    ,'{data.STATUS_TYPE}'
    ,{data.ERRO_LEITURA})";
                    numRowsInserted = _connection.Execute(sql, data);
                    transaction.Complete();
                }
            }
            catch (SqlException ex)
            {
                if (ex.Number == 2627 || ex.Number == 2601)
                {
                    _log.WarnFormat("Registro duplicado identificado durante inserção na tabela {0}. A operação será considerada concluída.", _tableName);
                    numRowsInserted = 1;
                }
                else
                {
                    _log.ErrorFormat("Erro ao executar inserção: {0}", ex.ToString().Replace(Environment.NewLine, string.Empty));
                }
            }
            catch (TransactionAbortedException ex)
            {
                _log.ErrorFormat("Inserção no banco abortada durante transação: {0}", ex.ToString().Replace(Environment.NewLine, string.Empty));
            }

            return numRowsInserted;
        }

        public bool UpdateStatusToDone(ColetaFuseData data)
        {
            bool lineUpdated;
            try
            {
                using (var transaction = new TransactionScope())
                {
                    string sql = $@"
UPDATE [dbo].[{_tableName}]
   SET [STATUS_TYPE] = 'DONE'
 WHERE [BOARDING_CODE] = '{data.BOARDING_CODE}'
	AND [PIER_CODE] = '{data.PIER_CODE}'
	AND [INCREMENT_NUMBER] = '{data.INCREMENT_NUMBER}'
";
                    lineUpdated = _connection.Execute(sql, data) >= 1;
                    transaction.Complete();
                }
            }
            catch (SqlException ex)
            {
                _log.ErrorFormat("Erro ao executar seleção: {}", ex.ToString().Replace(Environment.NewLine, string.Empty));
                lineUpdated = false;
            }
            catch (TransactionAbortedException ex)
            {
                _log.ErrorFormat("Seleção no banco abortada durante transação: {0}", ex.ToString().Replace(Environment.NewLine, string.Empty));
                lineUpdated = false;
            }

            return lineUpdated;
        }

        /// <summary>
        /// Count the amount of line that currently holds the status "pending"
        /// </summary>
        /// <returns>Value returned from query as integer. Negative if error happens.</returns>
        public int CountStatusPending()
        {
            try
            {
                using (var transaction = new TransactionScope())
                {
                    string sql = $@"
SELECT COUNT(*)
FROM [dbo].[{_tableName}]
WHERE [STATUS_TYPE] = 'PENDING'";
                    int response = _connection.ExecuteScalar<int>(sql);
                    transaction.Complete();
                    return response;
                }
            }
            catch (SqlException ex)
            {
                _log.ErrorFormat("Erro ao executar seleção: {0}", ex.ToString().Replace(Environment.NewLine, string.Empty));
            }
            catch (TransactionAbortedException ex)
            {
                _log.ErrorFormat("Seleção no banco abortada durante transação: {0}", ex.ToString().Replace(Environment.NewLine, string.Empty));
            }

            return -1;
        }
    }
}
