//-----------------------------------------------------------------------
// <copyright file="PgQuery.cs" company="Beckhoff Automation GmbH & Co. KG">
//     Copyright (c) Beckhoff Automation GmbH & Co. KG. All Rights Reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using TcHmiSrv.Core;
using TcHmiSrv.Core.General;
using TcHmiSrv.Core.Listeners;
using TcHmiSrv.Core.Tools.Management;
using System.Collections.Generic;
using Newtonsoft.Json;
using TcHmiSrv.Core.Tools.Json.Newtonsoft.Converters;
using TcHmiSrv.Core.Tools.Json.Newtonsoft;
using TcHmiSrv.Core.Listeners.ConfigListenerEventArgs;
using Npgsql;
using TcHmiSrv.Core.Tools.Json.Extensions;
using System.Xml;

namespace PgQuery
{
    record PgDiagnostics(string connectionState, string connectionError);
    record DatabaseConfig(string host, int port, string dbname, string userName, string password, int connectionTimeout);

    // Represents the default type of the TwinCAT HMI server extension.
    public class PgQuery : IServerExtension
    {
        private readonly RequestListener requestListener = new RequestListener();
        private readonly ConfigListener configListener = new ConfigListener();
        private PgDiagnostics diagnostics;
        private string query;
        private string connString;
        private NpgsqlDataSource dataSource;

        // Called after the TwinCAT HMI server loaded the server extension.
        public ErrorValue Init()
        {
            requestListener.OnRequest += OnRequest;
            configListener.OnChange += OnConfigChange;
            configListener.BeforeChange += OnBeforeConfigChange;
            LoadConfig();
            return ErrorValue.HMI_SUCCESS;
        }

        private void OnConfigChange(object sender, OnChangeEventArgs e)
        {
            if (e.Path == "databaseConnection")
                LoadConfig();
        }

        private void LoadConfig()
        {
            diagnostics = new PgDiagnostics("Config Change Pending", "");
            var config = TcHmiApplication.AsyncHost.GetConfigValue(TcHmiApplication.Context, "databaseConnection");
            var dbConfig = TcHmiJsonSerializer.Deserialize<DatabaseConfig>(config.ToJson(), false);
            
            connString = $"Host={dbConfig.host};UserName={dbConfig.userName};Password={ValueProtector.Decode(dbConfig.password)};Database={dbConfig.dbname}";
            dataSource = NpgsqlDataSource.Create(connString);

            try
            {
                var connection = dataSource.OpenConnection();
                if (connection.FullState == System.Data.ConnectionState.Open)
                    diagnostics = new PgDiagnostics("Connected", "");
            }
            catch (Exception ex)
            {
                diagnostics = new PgDiagnostics("Disconnected", ex.Message);
            }
        }

        private Value ExecuteQuery(string query)
        {
            var results = new List<Dictionary<string, object>>();
            using var command = dataSource.CreateCommand(query);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row.Add(reader.GetName(i), reader.GetValue(i));

                results.Add(row);
            }

            return TcHmiJsonSerializer.Deserialize(ValueJsonConverter.DefaultConverter, 
                JsonConvert.SerializeObject(results));
        }

        private void OnBeforeConfigChange(object sender, BeforeChangeEventArgs e)
        {
            if (e.Path == "databaseConnection::password")
            {
                var encoded = ValueProtector.Encode(e.Value.GetString());
                e.Value.SetValue(encoded);
            }
        }

        // Called when a client requests a symbol from the domain of the TwinCAT HMI server extension.
        private void OnRequest(object sender, TcHmiSrv.Core.Listeners.RequestListenerEventArgs.OnRequestEventArgs e)
        {
            try
            {
                e.Commands.Result = PgQueryErrorValue.PgQuerySuccess;

                foreach (Command command in e.Commands)
                {
                    try
                    {
                        // Use the mapping to check which command is requested
                        switch (command.Mapping)
                        {
                            case "Query":
                                query = command.WriteValue;
                                command.ExtensionResult = PgQueryErrorValue.PgQuerySuccess;
                                break;

                            case "Results":
                                command.ReadValue = ExecuteQuery(query);
                                command.ExtensionResult = PgQueryErrorValue.PgQuerySuccess;
                                break;

                            case "Diagnostics":
                                command.ReadValue = TcHmiJsonSerializer.Deserialize(ValueJsonConverter.DefaultConverter, JsonConvert.SerializeObject(diagnostics));
                                command.ExtensionResult = PgQueryErrorValue.PgQuerySuccess;
                                break;

                            default:
                                command.ExtensionResult = PgQueryErrorValue.PgQueryFail;
                                command.ResultString = "Unknown command '" + command.Mapping + "' not handled.";
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        command.ExtensionResult = PgQueryErrorValue.PgQueryFail;
                        command.ResultString = "Calling command '" + command.Mapping + "' failed! Additional information: " + ex.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new TcHmiException(ex.ToString(), ErrorValue.HMI_E_EXTENSION);
            }
        }
    }
}
