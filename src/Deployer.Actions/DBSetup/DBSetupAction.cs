using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CK.Core;
using Deployer.Action;
using Deployer.Settings;
using Deployer.Utils;
using Deployer.Utils.Rebindings;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Options;

namespace Deployer.Actions
{
    public class DBSetupAction : IAction
    {
        string _baseName;
        public string Description
        {
            get { return "Run the DBSetup to the configured database"; }
        }

        public IEnumerable<SubOptions> GetSubOptions()
        {
            return new SubOptions[]{
                new SubOptions(){ ArgumentName = "--from=", Description=@"DBSetup from a specific backup. Backup/Restore/DBSetup/Restore."},
                new SubOptions(){ ArgumentName = "--no-refresh", Description=@"DBSetup will not refresh views."},
                new SubOptions(){ ArgumentName = "--on-azure", Description=@"DBSetup target azure database."}
            };
        }

        public ISettings LoadSettings( ISettingsLoader loader, IList<string> extraParameters, IActivityMonitor logger )
        {
            return ConfigHelper.TryLoadCustomPathOrDefault( loader, extraParameters, logger );
        }

        public void CheckSettingsValidity( ISettings settings, IList<string> extraParameters, IActivityMonitor logger )
        {
            // Check rootdirectorypath
            if( !string.IsNullOrEmpty( settings.RootAbsoluteDirectory ) )
            {
                if( !Directory.Exists( settings.RootAbsoluteDirectory ) )
                {
                    logger.Error().Send( "The absolute root directory does not exists at {0}", settings.RootAbsoluteDirectory );
                }
            }
            else logger.Error().Send( "No absolute root directory configured" );

            // Check connection string
            if( !string.IsNullOrEmpty( settings.ConnectionString ) )
            {
                using( SqlConnection conn = new SqlConnection( settings.ConnectionString ) )
                {
                    try
                    {
                        conn.Open();
                    }
                    catch( Exception ex )
                    {
                        logger.Error().Send( ex, "Unable to connect to any server with the given connection string." );
                    }
                }
            }
            else logger.Error().Send( "No connection string configured" );

            // Check log directory
            if( !string.IsNullOrEmpty( settings.LogDirectory ) )
            {
                if( !Directory.Exists( Path.GetFullPath( settings.LogDirectory ) ) )
                {
                    Directory.CreateDirectory( Path.GetFullPath( settings.LogDirectory ) );
                    logger.Warn().Send( "Log directory not found. Automatically created." );
                }
            }
            else logger.Error().Send( "No log directory configured" );

            // Check dbsetup path
            if( !string.IsNullOrEmpty( settings.DBSetupConsolePath ) )
            {
                if( !File.Exists( Path.GetFullPath( settings.DBSetupConsolePath ) ) )
                {
                    logger.Error().Send( "DBSetup console not found at {0}", settings.DBSetupConsolePath );
                }
            }
            else logger.Error().Send( "No dbsetup console application configured" );


            // Check rootdirectorypath
            if( settings.DllDirectoryPaths != null && settings.DllDirectoryPaths.Count > 0 )
            {
                foreach( var p in settings.DllDirectoryPaths.Select( p => Path.GetFullPath( (p) ) ) )
                {
                    if( !Directory.Exists( p ) )
                    {
                        logger.Error().Send( "The dll directory does not exists at {0}", p );
                    }
                }
            }
            else logger.Error().Send( "No dll directory paths configured" );

            // Check AssemblieNamesToProcess
            if( settings.AssemblieNamesToProcess == null || settings.AssemblieNamesToProcess.Count == 0 )
                logger.Error().Send( "No assemblieNames to process configured" );

            string parsedBaseName = null;
            var options = new OptionSet() { { "from=", v => parsedBaseName = v } };

            try
            {
                options.Parse( extraParameters );
            }
            catch( Exception ex )
            {
                logger.Error().Send( "Error while parsing extra parameters" );
                logger.Error().Send( ex );
            }

            if( !string.IsNullOrWhiteSpace( parsedBaseName ) )
                _baseName = parsedBaseName;
        }

        public void Run( Runner runner, ISettings settings, IList<string> extraParameters, IActivityMonitor logger )
        {
            int innerErrorCount = 0;
            using( LogHelper.ReplicateIn( logger, settings, "DBSetups", string.Concat( "DBSetup-", DateTime.Now.ToFileFormatString(), ".log" ) ) )
            using( logger.OpenInfo().Send( "DBSetup" ) )
            {
                if( _baseName != null )
                    logger.Warn().Send( "You are running this dbsetup from a specific backup named '{0}'", _baseName );

                using( logger.CatchCounter( ( errorCount ) => innerErrorCount = errorCount ) )
                using( logger.OpenInfo().Send( "Backup" ) )
                {
                    if( !extraParameters.Contains( "--on-azure" ) )
                    {
                        runner.RunSpecificAction<BackupAction>( settings, extraParameters );
                    }
                    else
                    {
                        logger.Info().Send( "Run on azure (--on-azure). No backup !" );
                    }
                }

                if( innerErrorCount == 0 )
                {
                    if( _baseName != null )
                    {
                        using( logger.CatchCounter( ( errorCount ) => innerErrorCount = errorCount ) )
                        using( logger.OpenInfo().Send( "Restoring the specific backup '{0}'", _baseName ) )
                        {
                            runner.RunSpecificAction<RestoreAction>( settings, extraParameters );
                        }
                    }

                    IEnumerable<string> dllPaths;
                    using( logger.CatchCounter( ( errorCount ) => innerErrorCount = errorCount ) )
                        dllPaths = RelativizeDllsPaths( settings, logger );

                    if( innerErrorCount == 0 )
                    {
                        AssemblyNameDefinition ckCoreVersion = null;
                        using( logger.CatchCounter( ( errorCount ) => innerErrorCount = errorCount ) )
                        using( logger.OpenInfo().Send( "Check the version of CK.Core in the dll directories" ) )
                            ckCoreVersion = FindSpecificAssembly( "CK.Core", dllPaths, logger );

                        if( innerErrorCount == 0 )
                        {
                            if( ckCoreVersion != null )
                            {
                                UpdateAssemblyRebindingInDBSetupConsoleConfig( settings, ckCoreVersion, logger );
                            }

                            using( logger.OpenInfo().Send( "DBSetup process" ) )
                            {

                                if( innerErrorCount == 0 && CommandLineHelper.PromptBool( "Are you sure you want to run the dbsetup ?" ) )
                                {
                                    string commandline = string.Format( "-v2 \"{1}\" \"\" \"{2}\" \"{3}\" \"{4}\"",
                                        RemoveTrailingSlash( settings.DBSetupConsolePath ),
                                        RemoveTrailingSlash( settings.RootAbsoluteDirectory ),
                                        string.Join( ";", dllPaths ),
                                        string.Join( ";", settings.AssemblieNamesToProcess ),
                                        settings.ConnectionString );

                                    logger.Info().Send( "Running this command line {0}\"{1}\" {2}", Environment.NewLine, settings.DBSetupConsolePath, commandline );

                                    ProcessStartInfo processStartInfo = new ProcessStartInfo( settings.DBSetupConsolePath, commandline );
                                    processStartInfo.RedirectStandardOutput = true;
                                    processStartInfo.RedirectStandardError = true;
                                    processStartInfo.UseShellExecute = false;
                                    processStartInfo.CreateNoWindow = true;

                                    Process process = Process.Start( processStartInfo );

                                    ReadStdOut( logger, process.StandardOutput );
                                    //logger.Info().Send( process.StandardOutput.ReadToEnd() );

                                    string errors = process.StandardError.ReadToEnd();
                                    if( !string.IsNullOrWhiteSpace( errors ) )
                                        logger.Error().Send( errors );

                                    process.WaitForExit();
                                    process.Close();

                                    if( !extraParameters.Contains( "--no-refresh" ) )
                                    {
                                        using( logger.OpenInfo().Send( "Refresh views" ) )
                                        {
                                            RefreshView( settings, logger );
                                        }
                                    }

                                    if( _baseName != null )
                                    {
                                        using( logger.CatchCounter( ( errorCount ) => innerErrorCount = errorCount ) )
                                        using( logger.OpenInfo().Send( "Auto Revert based on backup done just before this dbsetup{0}(because you are running from a specific backup file)", Environment.NewLine ) )
                                        {
                                            runner.RunSpecificAction<RestoreAction>( settings, new string[0] );
                                        }
                                    }
                                }
                                else
                                {
                                    logger.Info().Send( "DBSetup aborted" );
                                }
                            }
                        }
                    }
                }
            }
        }

        void ReadStdOut(IActivityMonitor logger, StreamReader reader)
        {
            string str;
            while( (str = reader.ReadLine()) != null )
            {
                logger.Info().Send( str );
            }
        }

        AssemblyNameDefinition FindSpecificAssembly( string assemblyName, IEnumerable<string> dllPaths, IActivityMonitor logger )
        {
            AssemblyNameDefinition foundAssemblyName = null;
            foreach( var dllPath in dllPaths )
            {
                using( logger.OpenInfo().Send( "Looking for {0}.dll in {1}", assemblyName, dllPath ) )
                {
                    string foundPath =  Directory.EnumerateFiles( dllPath, string.Format( "{0}.dll", assemblyName ) ).FirstOrDefault();
                    if( !string.IsNullOrEmpty( foundPath ) )
                    {
                        logger.Info().Send( "{0} found : {1}", assemblyName, Path.GetFullPath( foundPath ) );
                        try
                        {
                            using( Stream dllStream = File.OpenRead( foundPath ) )
                            {
                                AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly( dllStream );
                                logger.Info().Send( "{0} reflected assembly name : {1}", assemblyName, assembly.Name );
                                if( foundAssemblyName == null )
                                    foundAssemblyName = assembly.Name;
                                else if( foundAssemblyName.ToString() != assembly.Name.ToString() ) // compare based on the tostring values of assembly name
                                    logger.Error().Send( "The {0} set of dll is not homogene. There is different versions here !", assemblyName );
                            }
                        }
                        catch( Exception ex )
                        {
                            logger.Error().Send( ex );
                        }
                    }
                }
            }

            return foundAssemblyName;
        }

        void UpdateAssemblyRebindingInDBSetupConsoleConfig( ISettings settings, AssemblyNameDefinition ckCoreVersion, IActivityMonitor logger )
        {
            new DBSetup.ConfigFileManipulator( settings, logger ).ResetAssemblyRebindings( ckCoreVersion );
        }

        IEnumerable<string> RelativizeDllsPaths( ISettings settings, IActivityMonitor logger )
        {
            List<string> paths = new List<string>();
            foreach( var path in settings.DllDirectoryPaths )
            {
                string commonAncestor = FindCommonPath( Path.GetFullPath( settings.RootAbsoluteDirectory ), Path.GetFullPath( path ) );
                if( string.IsNullOrEmpty( commonAncestor ) )
                    logger.Error().Send( "The dll directory path {0} has nothing in common with the root directory {1}", Path.GetFullPath( path ), Path.GetFullPath( settings.RootAbsoluteDirectory ) );

                string finalPath = RemoveTrailingSlash( Path.GetFullPath( path ).Remove( 0, commonAncestor.Length + 1 ) );

                if( !paths.Contains( finalPath ) )
                    paths.Add( finalPath );
            }

            return paths;
        }

        string RemoveTrailingSlash( string path )
        {
            if( path.EndsWith( "/" ) || path.EndsWith( "\\" ) )
                return path.Substring( 0, path.Length - 1 );
            return path;
        }

        void RefreshView( ISettings settings, IActivityMonitor logger )
        {
            using( SqlConnection conn = new SqlConnection( settings.ConnectionString ) )
            {
                try
                {
                    conn.Open();
                    conn.InfoMessage += ( o, e ) =>
                    {
                        logger.Info().Send( e.Message );
                    };

                    using( StreamReader sr = new StreamReader( Assembly.GetExecutingAssembly().GetManifestResourceStream( "Deployer.Actions.DBSetup.RefreshViews.sql" ) ) )
                    {
                        string sqlFile = sr.ReadToEnd();
                        using( var cmd = conn.CreateCommand() )
                        {
                            cmd.CommandText = sqlFile;
                            using( logger.OpenInfo().Send( "Starting refresh views process" ) )
                            {
                                cmd.ExecuteNonQuery();
                            }

                            logger.Info().Send( "All the database views have been refreshed" );
                        }
                    }
                }
                catch( Exception ex )
                {
                    logger.Error().Send( ex, "Unable to refresh the views." );
                }
            }
        }

        static string FindCommonPath( params string[] paths )
        {
            string separator = FileUtil.DirectorySeparatorString;

            string commonPath = String.Empty;
            List<string> separatedPath = paths
                .First( str => str.Length == paths.Max( st2 => st2.Length ) )
                .Split( new string[] { separator }, StringSplitOptions.RemoveEmptyEntries )
                .ToList();

            foreach( string pathSegment in separatedPath )
            {
                if( commonPath.Length == 0 && paths.All( str => str.StartsWith( pathSegment ) ) )
                    commonPath = pathSegment;
                else if( paths.All( str => str.StartsWith( commonPath + separator + pathSegment ) ) )
                    commonPath += separator + pathSegment;
                else
                    break;
            }

            return commonPath;
        }

    }
}
