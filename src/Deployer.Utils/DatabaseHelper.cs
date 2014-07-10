using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;

namespace Deployer.Utils
{
    public static class DatabaseHelper
    {
        public static bool TryToConnectToDB( string connectionString, IActivityMonitor logger )
        {
            try
            {
                using( SqlConnection conn = new SqlConnection( connectionString ) )
                {

                    conn.Open();
                    logger.Info().Send( "Test connection succeeded. The database {0} is reachable", conn.Database );

                    return true;
                }
            }
            catch( Exception ex )
            {
                logger.Error().Send( ex, "Unable to connect to any server with the given connection string." );
                return false;
            }
        }
    }
}
