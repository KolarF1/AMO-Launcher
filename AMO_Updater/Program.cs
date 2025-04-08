using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AMO_Updater
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("AMO Launcher Updater");
                Console.WriteLine("====================");

                if (args.Length < 3)
                {
                    Console.WriteLine("Error: Missing required arguments");
                    Console.WriteLine("Usage: AMO_Updater.exe <update_folder_path> <app_path> <process_id>");
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    return;
                }

                string updateFolderPath = args[0];
                string appPath = args[1];
                int processId = int.Parse(args[2]);

                string appDirectory = Path.GetDirectoryName(appPath);

                Console.WriteLine($"Update folder: {updateFolderPath}");
                Console.WriteLine($"App path: {appPath}");
                Console.WriteLine($"Process ID: {processId}");
                Console.WriteLine($"App directory: {appDirectory}");

                // Wait for the main application to close
                Console.WriteLine("Waiting for AMO Launcher to close...");
                await WaitForProcessToExitAsync(processId);

                // Small delay to ensure files are released
                await Task.Delay(1000);

                // Get version backup
                string appBackupPath = Path.Combine(appDirectory, "AMO_Launcher_backup.exe");
                File.Copy(appPath, appBackupPath, true);
                Console.WriteLine("Created backup of current version");

                // Replace files
                Console.WriteLine("Copying new files...");
                CopyDirectoryContents(updateFolderPath, appDirectory);

                Console.WriteLine("Update completed successfully!");
                Console.WriteLine("Restarting AMO Launcher...");

                // Restart the application
                Process.Start(appPath);

                // Exit the updater
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during update: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        private static async Task WaitForProcessToExitAsync(int processId)
        {
            try
            {
                Process process = null;

                try
                {
                    process = Process.GetProcessById(processId);
                }
                catch (ArgumentException)
                {
                    // Process already exited
                    return;
                }

                TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

                // Check if the process has already exited
                if (process.HasExited)
                {
                    return;
                }

                // Wait for process to exit with timeout
                process.EnableRaisingEvents = true;
                process.Exited += (sender, e) => tcs.TrySetResult(true);

                // Wait for the process to exit, or timeout after 60 seconds
                if (await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(60))) != tcs.Task)
                {
                    // Timeout occurred, try to kill the process
                    Console.WriteLine("Timeout waiting for application to close. Attempting to close it...");

                    if (!process.HasExited)
                    {
                        process.Kill();
                        await Task.Delay(2000); // Give it some time to exit
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error waiting for process: {ex.Message}");
            }
        }

        private static void CopyDirectoryContents(string sourceDir, string targetDir)
        {
            // Create all subdirectories
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourceDir, targetDir));
            }

            // Copy all files
            foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string destFilePath = filePath.Replace(sourceDir, targetDir);

                try
                {
                    // Skip updater.exe to prevent replacing itself
                    if (Path.GetFileName(destFilePath).Equals("AMO_Updater.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Skipping updater executable");
                        continue;
                    }

                    // Try several times in case the file is locked
                    int attempts = 0;
                    bool success = false;

                    while (!success && attempts < 5)
                    {
                        try
                        {
                            attempts++;
                            File.Copy(filePath, destFilePath, true);
                            success = true;
                        }
                        catch (IOException)
                        {
                            Console.WriteLine($"File locked, retrying: {destFilePath}");
                            Thread.Sleep(500);
                        }
                    }

                    if (!success)
                    {
                        Console.WriteLine($"Warning: Failed to copy file after multiple attempts: {destFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error copying file {filePath} to {destFilePath}: {ex.Message}");
                }
            }
        }
    }
}