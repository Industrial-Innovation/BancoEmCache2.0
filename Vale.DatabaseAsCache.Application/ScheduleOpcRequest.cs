using log4net;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using System;
using System.Configuration;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vale.DatabaseAsCache.Data.Repository;
using Vale.DatabaseAsCache.Data.TableModels;
using Vale.DatabaseAsCache.Service;
using Vale.DatabaseAsCache.Service.Infrastructure;
using Vale.GetFuseData.ApiService.Services;

namespace Vale.DatabaseAsCache.Application
{
    public class ScheduleOpcRequest : BackgroundService
    {
        /// <summary>
        /// Logger object
        /// </summary>
        private static readonly ILog _log = LogManager.GetLogger("log");

        /// <summary>
        /// Controlling interval valid values
        /// </summary>
        private readonly TimeSpan minimalInterval = TimeSpan.FromSeconds(5);
        private readonly TimeSpan maximalInterval = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Interval for trigger the pooling action
        /// </summary>
        private readonly TimeSpan _poolingInterval;

        /// <summary>
        /// Interface to connect into OpcAPI
        /// </summary>
        private readonly OpcApiInterface _opcApiInterface;

        /// <summary>
        /// Interface to connect into database
        /// </summary>
        private readonly ColetaFuseRepository _coletaFuseRepository;

        /// <summary>
        /// Application path to registry key
        /// </summary>
        private readonly string RegistryPath = WindowsRegistryInfo.BasePath + @"\ScheduleOpcRequest";

        /// <summary>
        /// Registry key to store the last value sent
        /// </summary>
        private readonly string RegistryKey = "LastValueSent";

        public ScheduleOpcRequest()
        {
            // Handling scheduler pooling interval
            if (!TimeSpan.TryParse(ConfigurationManager.AppSettings["SchedulerInterval"], out _poolingInterval))
            {
                _log.Error("Erro ao ler campo de configura��o do agendador: SchedulerInterval.");
                throw new FormatException();
            }

            if (TimeSpan.Compare(_poolingInterval, minimalInterval).Equals(-1) || TimeSpan.Compare(_poolingInterval, maximalInterval).Equals(1))
            {
                _log.Error($"Configura��o do agendador possui intervalo fora do aceit�vel: utilize intervalos entre {minimalInterval:c} e {maximalInterval:c}.");
                throw new FormatException();
            }

            // Handling OpcApiInterface options
            OpcApiOptions opcApiOptions = new OpcApiOptions()
            {
                OpcApiUrl = ConfigurationManager.AppSettings["OpcApiUrl"],
                HostName = ConfigurationManager.AppSettings["HostName"],
                ServerName = ConfigurationManager.AppSettings["ServerName"],
            };
            if (opcApiOptions.OpcApiUrl is null || opcApiOptions.HostName is null || opcApiOptions.ServerName is null)
            {
                _log.Error($"Erro ao ler campo de configura��o da API do OPC: {nameof(OpcApiOptions)}.");
                throw new FormatException();
            }
            _opcApiInterface = new OpcApiInterface(opcApiOptions);


            //Handling SqlServer repository connection
            if (ConfigurationManager.ConnectionStrings["Vale.Local.Cache"] != null)
            {
                string connectionStringMain = ConfigurationManager.ConnectionStrings["Vale.Local.Cache"].ConnectionString;
                Regex r = new Regex(@"(\b25[0-5]|\b2[0-4][0-9]|\b[01]?[0-9][0-9]?)(\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)){3}", RegexOptions.IgnoreCase);
                string serverIP = r.Match(connectionStringMain).ToString();

                _log.Info($"Estabelecendo conex�o com o banco de dados: {serverIP}");
                _coletaFuseRepository = new ColetaFuseRepository(connectionStringMain);
                if (!_coletaFuseRepository.IsConnectionOpen())
                {
                    _log.Error($"N�o foi poss�vel estabelecer conex�o com o banco!");
                    throw new FormatException();
                }
            }
            else
            {
                _log.Error($"Erro ao ler configura��o de conex�o ao banco de dados.");
                throw new FormatException();
            }

            _log.Info($"Intervalo entre requisi��es: {_poolingInterval:c}");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTime triggerStartTime = DateTime.Now;
                try
                {
                    _log.Info($"### Gatilho de leitura �s {triggerStartTime:dd/MM/yyyy HH:mm:ss} ###");
                    // VERIFICA SE TEM NOVO REGISTRO
                    string novoRegistro = _opcApiInterface.PostVerificaNovoRegistro();
                    if (novoRegistro is null)
                    {
                        _log.Info($"Erro ao requisitar verifica��o de novo registro");
                    }
                    else
                    {
                        if (OpcApiService.ConverteNovoRegistro(novoRegistro))
                        {
                            _log.Info($"Novo registro dispon�vel no CLP");
                            // BUSCA DADOS DO PIER
                            ColetaFuseData data = null;
                            string dataFromPier = _opcApiInterface.PostDataFromPier();
                            data = OpcApiService.ExtractDataFromPier(dataFromPier, triggerStartTime);
                            if (data != null)
                            {
                                // PERSISTE DADOS NO BANCO
                                int rowsInserted = _coletaFuseRepository.Insert(data);
                                if (rowsInserted > 0)
                                {
                                    _log.Info($"Dado foi salvo no banco!");
                                    _log.Debug($"{rowsInserted} dado(s) inserido(s): {data}");
                                    // ENVIA SINAL DE CONFIRMA��O DE ESCRITA AO OPC
                                    bool previousValueSent = (bool)(Registry.GetValue(RegistryPath, RegistryKey, 0) ?? 0);
                                    bool currentValueToSend = !previousValueSent;
                                    if (_opcApiInterface.PostSendConfirmationSignal(currentValueToSend))
                                    {
                                        Registry.SetValue(RegistryPath, RegistryKey, currentValueToSend);
                                        _log.Debug($"Sinal de confirma��o de escrita enviado com sucesso. Valor enviado: {currentValueToSend}");
                                    }
                                    else
                                    {
                                        _log.Error($"Erro ao enviar sinal confirma��o de escrita ao OPC. Tentativa de envio: {currentValueToSend}");
                                    }
                                }
                                else
                                {
                                    _log.Info($"Erro ao salvar dados no banco.");
                                }
                            }
                        }
                        else
                        {
                            _log.Info($"Nenhum registro dispon�vel no CLP");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"Erro no gatilho principal: {ex.ToString().Replace(Environment.NewLine, string.Empty)}");
                }
                // Garante que o intervalo entre requisi��es seja respeitado, mesmo com tempo de execu��o alto
                TimeSpan executionDuration = DateTime.Now - triggerStartTime;
                if (executionDuration < _poolingInterval)
                {
                    await Task.Delay(_poolingInterval - executionDuration, stoppingToken);
                }
            }
        }
    }
}