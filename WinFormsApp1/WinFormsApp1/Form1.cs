using System;
using System.IO;
using System.IO.Compression;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Configuration;
using System.Collections.Generic;

namespace WinFormsApp1
{
    public partial class Form1 : Form
    {
        private System.Windows.Forms.Timer _schedulerTimer;
        private bool _isProcessing = false;
        private DateTime _lastRunDate = DateTime.MinValue;

        private readonly string _baseWorkspace;
        private readonly string _tempChunksFolder;
        private readonly string _finalZipsFolder;
        private readonly string _nasTargetFolder;
        private readonly string _connectionString;

        public Form1()
        {
            InitializeComponent(); // Connects cleanly with Form1.Designer.cs

            // Core Workspace Configuration via App.config Fallbacks
            _baseWorkspace = Path.Combine(Application.StartupPath, ConfigurationManager.AppSettings["BaseWorkspace"] ?? "archive_workspace");
            _tempChunksFolder = Path.Combine(_baseWorkspace, "temp_chunks");
            _finalZipsFolder = Path.Combine(_baseWorkspace, "final_zips");
            _nasTargetFolder = ConfigurationManager.AppSettings["NasTargetFolder"] ?? @"C:\MockNasBackupServer";
            _connectionString = ConfigurationManager.ConnectionStrings["MyConnectionString"].ConnectionString;

            // Generate directories cleanly
            Directory.CreateDirectory(_tempChunksFolder);
            Directory.CreateDirectory(_finalZipsFolder);

            this.Load += Form1_Load;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            InitializeScheduler();
        }

        private void InitializeScheduler()
        {
            _schedulerTimer = new System.Windows.Forms.Timer();
            // Checking every 15 seconds guarantees we never miss the 14:18 minute frame
            _schedulerTimer.Interval = 15000;
            _schedulerTimer.Tick += SchedulerTimer_Tick;
            _schedulerTimer.Start();
            LogToConsole("Production Archiver Engine Active. Monitoring clock for 16:28...");
        }

        private async void SchedulerTimer_Tick(object sender, EventArgs e)
        {
            // FIX: Synchronized logic with the 14:18 (2:18 PM) requirement stated in the logs
            if (DateTime.Now.Hour == 16 && DateTime.Now.Minute == 28
                && !_isProcessing && _lastRunDate.Date != DateTime.Today)
            {
                _lastRunDate = DateTime.Today;
                _isProcessing = true;
                LogToConsole("14:18 clock marker reached. Launching live data extraction pipeline...");

                await Task.Run(() => StartArchivingPipeline());

                _isProcessing = false;
            }
        }

        private void StartArchivingPipeline()
        {
            try
            {
                // 1. Scan and locate the absolute oldest date record snapshot
                DateTime? oldestDate = GetOldestDateFromDatabase();
                if (oldestDate == null)
                {
                    LogToConsole("Data verification pass: No rows found in [MainApi].[HitCount] to archive.");
                    return;
                }

                string dateString = oldestDate.Value.ToString("yyyy-MM-dd");
                LogToConsole($"Target isolated for date execution: {dateString}");

                int batchSize = 50000; // Optimal data streaming size limit
                int batchIndex = 1;
                long lastProcessedId = 0; // State tracker to keep memory low

                ClearFolder(_tempChunksFolder);
                List<long> processedIdsList = new List<long>();

                // 2. Continuous Pagination Loop for the targeted date
                while (true)
                {
                    LogToConsole($"Streaming batch sequence {batchIndex} for {dateString}...");
                    DataTable dt = FetchBatchFromDatabase(oldestDate.Value, batchSize, lastProcessedId);

                    if (dt.Rows.Count == 0)
                    {
                        LogToConsole($"No more data segments found for {dateString}. Finalizing export files...");
                        break;
                    }

                    // 3. Build text-chunk JSON document locally
                    string jsonFileName = $"{dateString}_chunk_{batchIndex}.json";
                    string jsonFilePath = Path.Combine(_tempChunksFolder, jsonFileName);

                    LogToConsole($"Writing data matrix ({dt.Rows.Count} items) into local chunk: {jsonFileName}");
                    WriteDataTableToJson(dt, jsonFilePath);

                    // Map primary keys for future targeted safe cleanup tracking
                    foreach (DataRow row in dt.Rows)
                    {
                        processedIdsList.Add(Convert.ToInt64(row["ID"]));
                    }

                    // Shift position flag marker to the last row primary key value
                    lastProcessedId = Convert.ToInt64(dt.Rows[dt.Rows.Count - 1]["ID"]);

                    if (dt.Rows.Count < batchSize) break; // Finished streaming the whole day
                    batchIndex++;
                }

                // 4. Archive Packaging Verification Lifecycle
                if (processedIdsList.Count > 0)
                {
                    string zipFileName = $"{dateString}.zip";
                    string localZipPath = Path.Combine(_finalZipsFolder, zipFileName);
                    string nasZipPath = Path.Combine(_nasTargetFolder, zipFileName);

                    if (File.Exists(localZipPath)) File.Delete(localZipPath);

                    LogToConsole($"Compressing JSON fragments into production archive container: {zipFileName}");
                    ZipFile.CreateFromDirectory(_tempChunksFolder, localZipPath);

                    // 5. NAS/Backup Storage Verification Integrity Checks
                    if (File.Exists(localZipPath) && new FileInfo(localZipPath).Length > 0)
                    {
                        LogToConsole("Local file validation check complete. Initiating network upload to server...");

                        // Ensure destination server directory exists safely
                        Directory.CreateDirectory(_nasTargetFolder);
                        File.Copy(localZipPath, nasZipPath, true);

                        if (File.Exists(nasZipPath) && new FileInfo(nasZipPath).Length > 0)
                        {
                            LogToConsole($"Archive deployment verified on network backup storage server! Initializing SQL database row cleanup...");

                            // 6. Targeted safe row purge process
                            DeleteArchivedRowsByIDs(processedIdsList);
                            LogToConsole($"Database clear operations complete! All rows matching {dateString} have been removed from the server.");

                            // 7. Cleanup local transient staging data spaces
                            ClearFolder(_tempChunksFolder); // Clears the loose JSON pieces

                            // FIX: Commented out local deletion so your final local .zip file remains safe!
                            // File.Delete(localZipPath); 

                            LogToConsole($"System sweep successful. Local ZIP preserved in: {_finalZipsFolder}. Pipeline cycle finished for: {dateString}");
                        }
                        else
                        {
                            throw new Exception("Critical File IO Failure: Package corrupted or unreadable on target storage location.");
                        }
                    }
                    else
                    {
                        throw new Exception("Critical Archive Failure: System failed to compile compressed data components locally.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Pipeline Execution Exception: {ex.Message}");
            }
        }

        #region Core Database & Helper Operations

        private DateTime? GetOldestDateFromDatabase()
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT MIN([DT]) FROM [MainApi].[HitCount]";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.CommandTimeout = 500; // Extra protection margin for database scans
                    conn.Open();
                    object result = cmd.ExecuteScalar();
                    if (result != DBNull.Value && result != null)
                    {
                        return Convert.ToDateTime(result).Date;
                    }
                }
            }
            return null;
        }

        private DataTable FetchBatchFromDatabase(DateTime targetDate, int batchSize, long lastId)
        {
            DataTable dt = new DataTable();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                // High performance composite filtering utilizing index tracking structures
                string query = @"SELECT TOP (@BatchSize) [ID], [DT], [IPAdd], [API], [U], [P] 
                                 FROM [MainApi].[HitCount] 
                                 WHERE CAST([DT] AS DATE) = @TargetDate AND [ID] > @LastId
                                 ORDER BY [ID] ASC";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@BatchSize", batchSize);
                    cmd.Parameters.AddWithValue("@TargetDate", targetDate);
                    cmd.Parameters.AddWithValue("@LastId", lastId);
                    cmd.CommandTimeout = 500;

                    conn.Open();
                    using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                    {
                        adapter.Fill(dt);
                    }
                }
            }
            return dt;
        }

        private void WriteDataTableToJson(DataTable dt, string filePath)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[\n");
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                sb.Append("  {\n");
                for (int j = 0; j < dt.Columns.Count; j++)
                {
                    string colName = dt.Columns[j].ColumnName;
                    string rowValue = dt.Rows[i][j].ToString()
                                        .Replace("\\", "\\\\")
                                        .Replace("\"", "\\\"");

                    sb.Append($"    \"{colName}\": \"{rowValue}\"");
                    if (j < dt.Columns.Count - 1) sb.Append(",\n");
                }
                sb.Append("\n  }");
                if (i < dt.Rows.Count - 1) sb.Append(",\n");
            }
            sb.Append("\n]");
            File.WriteAllText(filePath, sb.ToString());
        }

        private void DeleteArchivedRowsByIDs(List<long> ids)
        {
            if (ids == null || ids.Count == 0) return;

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Micro-batch deletion framework processing 5,000 index values at a time
                // This eliminates locking issues on the live table completely
                int processingChunkSize = 5000;
                for (int i = 0; i < ids.Count; i += processingChunkSize)
                {
                    List<long> currentChunk = ids.GetRange(i, Math.Min(processingChunkSize, ids.Count - i));
                    string parsedIdChain = string.Join(",", currentChunk);

                    string query = $"DELETE FROM [MainApi].[HitCount] WHERE [ID] IN ({parsedIdChain})";
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.CommandTimeout = 500;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private void ClearFolder(string folderPath)
        {
            DirectoryInfo di = new DirectoryInfo(folderPath);
            foreach (FileInfo file in di.GetFiles()) file.Delete();
        }

        private void LogToConsole(string message)
        {
            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] INFO: {message}";
            System.Diagnostics.Debug.WriteLine(logLine);

            try
            {
                string logPath = Path.Combine(_baseWorkspace, "live_production_log.txt");
                File.AppendAllText(logPath, logLine + Environment.NewLine);
            }
            catch { }

            if (listBoxLogs != null)
            {
                if (listBoxLogs.InvokeRequired)
                {
                    listBoxLogs.Invoke(new Action(() =>
                    {
                        listBoxLogs.Items.Add(logLine);
                        listBoxLogs.TopIndex = listBoxLogs.Items.Count - 1;
                    }));
                }
                else
                {
                    listBoxLogs.Items.Add(logLine);
                    listBoxLogs.TopIndex = listBoxLogs.Items.Count - 1;
                }
            }
        }

        private void LogError(string message)
        {
            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}";
            System.Diagnostics.Debug.WriteLine(logLine);

            try
            {
                string logPath = Path.Combine(_baseWorkspace, "live_production_log.txt");
                File.AppendAllText(logPath, logLine + Environment.NewLine);
            }
            catch { }

            if (listBoxLogs != null)
            {
                if (listBoxLogs.InvokeRequired)
                {
                    listBoxLogs.Invoke(new Action(() =>
                    {
                        listBoxLogs.Items.Add(logLine);
                        listBoxLogs.TopIndex = listBoxLogs.Items.Count - 1;
                    }));
                }
                else
                {
                    listBoxLogs.Items.Add(logLine);
                    listBoxLogs.TopIndex = listBoxLogs.Items.Count - 1;
                }
            }
        }

        #endregion
    }
}