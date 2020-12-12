﻿using EasySave.NS_Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EasySave.NS_ViewModel
{
    public class MenuViewModel
    {
        // ----- Attributes -----
        public Model model { get; set; }
        private object _sync = new object();

        // ----- Constructor -----
        public MenuViewModel(Model _model)
        {
            this.model = _model;
        }


        // ----- Methods -----
        private List<Work> getWorksById(int[] _worksId)
        {
            List<Work> works = new List<Work>();

            foreach (int workId in _worksId)
            {
                works.Add(model.works[workId]);
            }
            return works;
        }

        private bool IsWorkSelected(int[] _worksId) // TODO - Look if we can do better or did we let this here
        {
            if (_worksId.Length > 0)
            {
                return true;
            }
            else
            {
                // Return Error Code
                model.errorMsg?.Invoke("noSelectedWork");
                return false;
            }
        }

        public void RemoveWorks(int[] _worksId)
        {
            if (IsWorkSelected(_worksId))
            {
                List<Work> worksToRemove = getWorksById(_worksId);

                foreach (Work workToRemove in worksToRemove)
                {
                    RemoveWork(workToRemove);
                }
            }
        }

        private void RemoveWork(Work _workToRemove)
        {
            try
            {
                // Remove Work from the program (at index)
                this.model.works.Remove(_workToRemove);
                this.model.SaveWorks();
            }
            catch
            {
                // Return Error Code
                model.errorMsg?.Invoke("errorRemoveWork");
            }
        }

        public void LaunchBackupWork(int[] _worksId)
        {
            if (IsWorkSelected(_worksId))
            {
                List<Work> worksToSave = getWorksById(_worksId);

                if (IsBusinessRunning())
                {
                    // Return Error Code
                    model.errorMsg?.Invoke("businessSoftwareOn"); // TODO - "Cannot launch any backups bc business software ON"
                    return;
                }

                // Launch Backup
                foreach (Work workToSave in worksToSave)
                {
                    Task.Run(() =>
                    {
                        SaveWork(workToSave);
                    });
                }
            }
        }

        private void SaveWork(Work _workToSave)
        {
            // Take destination disk
            DriveInfo dstDisk = new DriveInfo(_workToSave.dst.Substring(0, 1));

            // Check if current and destination folder and destination disk are correct && Check If the folder can be crypted (true if not crypted work)
            if (IsSaveDirsCorrect(_workToSave.src, _workToSave.dst) && IsDiskReady(dstDisk) && IsEncryptionPossible(_workToSave))
            {
                // Get every files info to copy
                FileInfo[] filesToSave = GetFilesToSave(_workToSave);
                string dstFolder = _workToSave.dst + _workToSave.name + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "\\";

                // Check if there is files to copy, enough place in dst folder and we can create destination folder
                if (IsFilesToSave(filesToSave.Length) && IsSpaceInDstDir(dstDisk, 0) && InitDstFolder(dstFolder))
                {
                    // Save every file and get back the failed files
                    List<string> failedFiles = SaveFiles(_workToSave, filesToSave, dstFolder);

                    // If there is any errors
                    if (failedFiles.Count != 0)
                    {
                        // Return Error Code
                        model.errorMsg?.Invoke("backupFinishedWithError");
                    }
                }
                // Reset the work object
                _workToSave.state = null;
                _workToSave.lastBackupDate = DateTime.Now.ToString("yyyy/MM/dd_HH:mm:ss");
                this.model.SaveWorks();

                Trace.WriteLine(_workToSave.name + " finished " + DateTime.Now.ToString("yyyy/MM/dd_HH:mm:ss"));
            }
        }

        private bool IsEncryptionPossible(Work _work)
        {
            if (!(_work.isCrypted && this.model.settings.cryptoSoftPath.Length == 0))
            {
                return true;
            }
            else
            {
                // Return Error Code
                model.errorMsg?.Invoke("cryptoSoftPathNotFound");
                return false;
            }
        }

    private bool IsFilesToSave(int _nbFilesToSave)
        {
            if (_nbFilesToSave > 0)
            {
                return true;
            }
            else
            {
                // Return Error Code
                model.errorMsg?.Invoke("noChangeSinceLastBackup");
                return false;
            }
        }

        private bool InitDstFolder(string _dstFolder)
        {
            // Create the dst folder
            try
            {
                Directory.CreateDirectory(_dstFolder);
                return true;
            }
            catch
            {
                // Return Error Code
                model.errorMsg?.Invoke("cannotCreateDstFolder");
                return false;
            }
        }

        private bool IsBusinessRunning()
        {
            foreach (string businessSoftware in this.model.settings.businessSoftwares)
            {
                if (Process.GetProcessesByName(businessSoftware).Length > 0)
                {
                    return true; ;
                }
            }
            return false;
        }

        private bool IsDiskReady(DriveInfo _dstDisk)
        {
            if(_dstDisk.IsReady)
            {
                return true;
            }
            else
            {
                // Return Error Code
                model.errorMsg?.Invoke("diskError");
                return false;
            }
        }

        private bool IsSpaceInDstDir(DriveInfo _dstDisk, long _totalSize)
        {
            if (_dstDisk.TotalFreeSpace > _totalSize)
            {
                return true;
            }
            else
            {
                // Return Error Code
                model.errorMsg?.Invoke("noSpaceDstFolder");
                return false;
            }
        }

        private bool IsSaveDirsCorrect(string _src, string _dst)
        {
            // Check if the source exists
            if (!Directory.Exists(_src))
            {
                // Return Error Code
                model.errorMsg?.Invoke("unavailableSrcPath");
                return false;
            }

            // Check if the destionation folder exists
            if (!Directory.Exists(_dst))
            {
                // Return Error Code
                model.errorMsg?.Invoke("unavailableDstPath");
                return false;
            }
            return true;
        }

        private FileInfo[] GetFilesToSave(Work _work) // TODO - Total Size
        {
            long totalSize = 0;

            // Get evvery files of the source directory
            DirectoryInfo srcDir = new DirectoryInfo(_work.src);
            FileInfo[] srcFiles = srcDir.GetFiles("*.*", SearchOption.AllDirectories);

            switch (_work.backupType)
            {
                case BackupType.FULL:
                    // Calcul the size of every files
                    foreach (FileInfo file in srcFiles)
                    {
                        totalSize += file.Length;
                    }

                    // Init the state of the current work to save
                    _work.state = new State(srcFiles.Length, totalSize, "", "");
                    return srcFiles;

                case BackupType.DIFFRENTIAL:
                    // Get all directories name of the dest folder
                    DirectoryInfo[] dirs = new DirectoryInfo(_work.dst).GetDirectories();
                    string lastFullDirName = GetFullBackupDir(dirs, _work.name);

                    // If there is no full backup as a ref, we create the first one as full backup
                    if (lastFullDirName.Length == 0) goto case BackupType.FULL;

                    // Get evvery files of the source directory
                    List<FileInfo> filesToSave = new List<FileInfo>();

                    // Check if there is a modification between the current file and the last full backup
                    foreach (FileInfo file in srcFiles)
                    {
                        string currFullBackPath = lastFullDirName + "\\" + Path.GetRelativePath(_work.src, file.FullName);

                        if (!File.Exists(currFullBackPath) || !IsSameFile(currFullBackPath, file.FullName))
                        {
                            // Calcul the size of every files
                            totalSize += file.Length;

                            // Add the file to the list
                            filesToSave.Add(file);
                        }
                    }

                    // Init the state of the current work to save
                    _work.state = new State(filesToSave.Count, totalSize, "", "");
                    return filesToSave.ToArray();

                default:
                    model.errorMsg?.Invoke("unavailableBackupType");
                    return new FileInfo[0];
            }
        }

        // Get the directory name of the first full backup of a differential backup
        private string GetFullBackupDir(DirectoryInfo[] _dstDirs, string _name)
        {
            foreach (DirectoryInfo directory in _dstDirs)
            {
                if (directory.Name.IndexOf("_") > 0 && _name == directory.Name.Substring(0, directory.Name.IndexOf("_")))
                {
                    return directory.FullName;
                }
            }
            return "";
        }

        // Check if the file or the src is the same as the full backup one to know if the files need to be copied or not
        private bool IsSameFile(string path1, string path2)
        {
            byte[] file1 = File.ReadAllBytes(path1);
            byte[] file2 = File.ReadAllBytes(path2);

            if (file1.Length == file2.Length)
            {
                for (int i = 0; i < file1.Length; i++)
                {
                    if (file1[i] != file2[i])
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        private List<string> SaveFiles(Work _work, FileInfo[] _filesToSave, string _dstFolder)
        {
            // Create a name list of failed files
            List<string> failedFiles = new List<string>();
            int totalFile = _work.state.totalFile;

            // Save file one by one
            for (int i = 0; i < totalFile; i++)
            {
                // Get the current file to save
                FileInfo curFile = _filesToSave[i];

                DateTime startTimeSave = DateTime.Now;
                int copyTime = 0;
                int encryptionTime = 0;
                int pourcent = (i * 100 / totalFile);
                int fileRemaining = totalFile - i;
                string dstFile = GetDstFilePath(curFile, _dstFolder, _work.src);


                // Update the current work status
                _work.state.UpdateState(pourcent, fileRemaining, _work.state.leftSize, curFile.FullName, dstFile);
                if (Monitor.TryEnter(_sync, 100))
                {
                    this.model.SaveWorks();
                    Monitor.Exit(_sync);
                }

                // Check if the file is crypted or not
                if (!(_work.isCrypted && curFile.Name.Contains(".") && this.model.settings.cryptoExtensions.Contains(curFile.Name.Substring(curFile.Name.LastIndexOf(".")))))
                {
                    // Save File
                    copyTime = CopyFile(curFile, dstFile, startTimeSave);
                }
                else
                {
                    // Crypt File
                    encryptionTime = EncryptFile(curFile, dstFile);
                }

                // Add Current Backuped File Log
                this.model.logs.Add(new Log($"{_work.name}", $"{curFile.FullName}", $"{dstFile}", $"{curFile.Length}", $"{startTimeSave}", $"{copyTime}", $"{encryptionTime}"));
                if (Monitor.TryEnter(_sync, 100))
                {
                    this.model.SaveLog();
                    Monitor.Exit(_sync);
                }

                Trace.WriteLine($"{_work.name} {curFile.FullName} {dstFile} {curFile.Length} {startTimeSave} {copyTime} {encryptionTime}");
                //Thread.Sleep(5000);
            }

            // End of the current work
            _work.state.UpdateState(100, 0, 0, "", "");
            return failedFiles;
        }


        private string GetDstFilePath(FileInfo _srcFile, string _dst, string _src)
        {
            string curDirPath = _srcFile.DirectoryName;
            string dstDirectory = _dst;

            // If there is a directoy, we add the relative path from the directory dst
            if (Path.GetRelativePath(_src, curDirPath).Length > 1)
            {
                dstDirectory += Path.GetRelativePath(_src, curDirPath) + "\\";

                // If the directory dst doesn't exist, we create it
                if (!Directory.Exists(dstDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(dstDirectory);
                    }
                    catch
                    {
                        // If cannot create the dst path
                        // TODO - Error Msg
                        return "";
                    }
                }
            }

            // Return the current dstFile
            return dstDirectory + _srcFile.Name;
        }

        private int CopyFile(FileInfo _curFile, string _dstFile, DateTime _startTime)
        {
            try
            {
                _curFile.CopyTo(_dstFile, true);
                return (int) (DateTime.Now - _startTime).TotalMilliseconds;
            }
            catch
            {
                return -1;
            }
        }

        private int EncryptFile(FileInfo _curFile, string _dstFile)
        {
            try
            {
                ProcessStartInfo process = new ProcessStartInfo(this.model.settings.cryptoSoftPath);
                process.Arguments = "source " + _curFile.FullName + " destination " + _dstFile;
                var proc = Process.Start(process);
                proc.WaitForExit();
                return proc.ExitCode;
            }
            catch
            {
                return -1;
            }
        }
    }
}