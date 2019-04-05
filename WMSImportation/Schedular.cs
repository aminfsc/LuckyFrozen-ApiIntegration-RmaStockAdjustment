using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ice.Core;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Reflection;
using System.Net;
//using Erp.Tables;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WMSImportation
{
    class Schedular
    {
        System.Timers.Timer oTimer = null;
        double interval = 20000;
        bool isExecutionCompleted = true;
        string sourcePath = string.Empty; //ConfigurationManager.AppSettings["SourcePath"];
        string successLocation = string.Empty; //ConfigurationManager.AppSettings["SuccessLocation"];
        string failedLocation = string.Empty; //ConfigurationManager.AppSettings["FailedLocation"];
        string companies = ConfigurationManager.AppSettings["Company"]; //Amin
        StringBuilder sbFileLog = null;
        DataAccessLayer oDAL = new DataAccessLayer();
        public Schedular()
        {
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, errors) => { return true; };
        }

        public void Start()
        {
            oTimer = new System.Timers.Timer(interval);
            oTimer.AutoReset = true;
            oTimer.Enabled = true;
            oTimer.Start();

            oTimer.Elapsed += oTimer_Elapsed;
        }
        void oTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!isExecutionCompleted)
                return;
            try
            {
                string[] lstCompanies = companies.Split(','); //Amin
                foreach (string comp in lstCompanies)
                {
                    sourcePath = ConfigurationManager.AppSettings[comp + "SourcePath"];
                    failedLocation = ConfigurationManager.AppSettings[comp + "FailedLocation"];
                    successLocation = ConfigurationManager.AppSettings[comp + "SuccessLocation"];

                    isExecutionCompleted = false;
                    FileCreation("");
                    GetCsvFiles();
                }


            }
            catch (Exception ex)
            {
                //string errMsg = Logger.Log(ex, eventId++);
                FileCreation(ex.Message.ToString());
                ErrorInfoFile(sourcePath, ex.Message.ToString());
                
            }
            finally
            {
                isExecutionCompleted = true;
            }
        }
        void FileCreation(string errMsg)
        {
            //string sFile = @"C:\ExternalPrograms\WMS Importation\WMSImportation\OnStart.txt";
            string sFile = @"OnStart.txt";
            string destPath = AppDomain.CurrentDomain.BaseDirectory + sFile;

            string sDateTime = DateTime.Now.ToString();

            System.IO.StreamWriter oFileWriter = new System.IO.StreamWriter(destPath, true);
            oFileWriter.WriteLine("\n" + sDateTime);
            oFileWriter.WriteLine("\n" + errMsg);

            oFileWriter.Close();
        }
        void GetCsvFiles()
        {
            if (Directory.Exists(sourcePath))
            {
                //Get count of All .CSV files files from source path

                int filesCount = Directory.GetFiles(sourcePath, "*.CSV*").Length;
                //sbFileLog.AppendLine("FileCount =" + filesCount.ToString() + " in " + sourcePath);
                if (filesCount > 0)
                {
                    //All csv files storing into files array.
                    string[] files = Directory.GetFiles(sourcePath, "*.CSV*");
                    foreach (string file in files)
                    {

                        //Get All data from file to csvDt datatable
                        DataTable csvDt = new DataTable();
                        csvDt = CsvToDataTable(file);

                        //Pass the csvDt datatable for data importation to DB
                        if (csvDt.Rows.Count > 0)
                        {
                            sbFileLog = new StringBuilder();
                            bool Error = false;
                            try
                            {

                                sbFileLog.AppendLine("**********************************");
                                sbFileLog.AppendLine(DateTime.Now.ToString("yyyyMMddHHmmss"));
                                csvStockAdjustment(csvDt, file);
                                sbFileLog.AppendLine(DateTime.Now.ToString("yyyyMMddHHmmss"));
                                sbFileLog.AppendLine("**********************************");
                            }
                            catch (Exception ex)
                            {
                                sbFileLog.AppendLine(Logger.Log(ex, eventId++));
                                Error = true;
                                throw new Exception(ex.ToString());

                            }
                            finally
                            {
                                if (!Error)
                                {
                                    WriteLogInfoFile(successLocation, file, sbFileLog);
                                }
                                else
                                {
                                    WriteLogInfoFile(failedLocation, file, sbFileLog);
                                }
                                if (sbFileLog != null)
                                {
                                    sbFileLog.Clear();
                                }
                            }
                        }
                        else
                        {
                            //csvDt File doesnot have rows.
                            throw new Exception(file + "  - File doesnot have rows.");

                        }

                    }
                }
                else
                {
                    //file count is 0
                    throw new Exception("No Files found in this path : "+sourcePath);
                }
            }
            else
            {
                throw new Exception("Source Path Not found : " + sourcePath);
            }

        }
        public DataTable CsvToDataTable(string file)
        {
            DataTable dtFile = null;
            StreamReader oStreamReader = null;
            try
            {
                if (!IsFileLocked(file))
                {
                    oStreamReader = new StreamReader(file);
                    //DataTable dtFile = null;
                    int rowCount = 1;
                    string[] columnNames = null;
                    string[] oStreamDataValues = null;

                    while (!oStreamReader.EndOfStream)
                    {
                        string oStreamRowData = oStreamReader.ReadLine().Trim();
                        //split by seperator within the first read line string
                        //char[] stringSeparators = new char[] {  };
                        oStreamDataValues = oStreamRowData.Split('|');
                        //row = oStreamRowData.Split('|');
                        //oStreamDataValues = oStreamDataValues.Select(s => s.Replace("\"", "")).ToArray();

                        if (oStreamDataValues[0].Length > 0)
                        {
                            if (rowCount == 1)
                            {
                                rowCount = 3;
                                columnNames = oStreamDataValues;
                                dtFile = new DataTable();
                                foreach (string column in columnNames)
                                {
                                    DataColumn oDataColumn = new DataColumn(column.Replace("\"", "").ToUpper(), typeof(string));
                                    oDataColumn.DefaultValue = string.Empty;
                                    dtFile.Columns.Add(oDataColumn);

                                }
                            }
                            else
                            {
                                if (columnNames[0] != oStreamDataValues[0])
                                {
                                    DataRow oDataRow = dtFile.NewRow();
                                    for (int i = 0; i < columnNames.Length; i++)
                                    {
                                        oDataRow[columnNames[i].Replace("\"", "")] = oStreamDataValues[i] == null ? string.Empty : oStreamDataValues[i].ToString();

                                    }
                                    dtFile.Rows.Add(oDataRow);


                                }
                            }
                        }
                        else
                        {
                            //oStreamVlaues length is zero
                        }
                    }

                    oStreamReader.Close();

                }
                else
                {
                    //File is in Open Mode
                    oStreamReader.Close();
                    throw new System.UnauthorizedAccessException("File Name = " + file + " is read-only");
                }
            }
            catch (System.IndexOutOfRangeException e)
            {
                // Set IndexOutOfRangeException to the new exception's InnerException.
                oStreamReader.Close();
                StringBuilder csvSbLog = new StringBuilder();
                csvSbLog.AppendLine("FileName = " + file);
                csvSbLog.AppendLine("Message: Column  and Data Values count is not matching!");
                MoveFile(failedLocation, file);
                WriteLogInfoFile(failedLocation, file, csvSbLog);
                throw new System.ArgumentOutOfRangeException("FileName = " + file + "      Message: Column  and Data Values count is not matching!", e);
            }

            return dtFile;

        }

       
        public void csvStockAdjustment(DataTable dtCsvStkAdj, string file)
        { 
             bool isSoUpdSuccess = false;
             string UserId = ConfigurationManager.AppSettings["UserId"];
             string Password = ConfigurationManager.AppSettings["Password"];
             string Company = ConfigurationManager.AppSettings["Company"];
             string Plant = ConfigurationManager.AppSettings["Plant"];
             svcInvQtyAdj.InventoryQtyAdjSvcContractClient svcInvStkAdjClient = new svcInvQtyAdj.InventoryQtyAdjSvcContractClient("BasicHttpBinding_InventoryQtyAdjSvcContract");
             Ice.Core.Session oConn = new Ice.Core.Session(UserId, Password);
             oConn.CompanyID = Company;
             oConn.PlantID = Plant;
             svcInvStkAdjClient.ClientCredentials.UserName.UserName = UserId;
             svcInvStkAdjClient.ClientCredentials.UserName.Password = Password;
             svcInvStkAdjClient.Endpoint.EndpointBehaviors.Add(new HookServiceBehavior(new Guid(oConn.SessionID), UserId));
             svcInvQtyAdj.InventoryQtyAdjTableset dsInvStkAdj = null;
             try
             {
                 
                 //svcInvQtyAdj.InventoryQtyAdjBrwTableset dsInvStkAdjBrw = null;
                 var distCsvStkAdjs = dtCsvStkAdj.AsEnumerable().Select(s => new
                                                                {
                                                                    plant = s.Field<string>("Plant"),
                                                                    comp = s.Field<string>("Company"),
                                                                    partNo = s.Field<string>("PartNum").Trim(),
                                                                    wareHseCode = s.Field<string>("WareHseCode"),
                                                                    binNo = s.Field<string>("BinNum"),
                                                                    adjQty = Convert.ToDecimal(s.Field<string>("AdjustQuantity")),
                                                                    uom = s.Field<string>("UnitOfMeasure"),
                                                                    reasonCode = s.Field<string>("ReasonCode"),
                                                                    transDate = Convert.ToDateTime(s.Field<string>("TransDate")),
                                                                    reference = s.Field<string>("Reference")
                                                                }).Distinct().ToList();

                 foreach (var stkAdjrow in distCsvStkAdjs)
                 {
                     string fileName = Path.GetFileNameWithoutExtension(file);
                     int cnt = oDAL.RowExistInWMSStockAdjHistory(fileName,stkAdjrow.plant,stkAdjrow.comp,stkAdjrow.partNo,stkAdjrow.wareHseCode,stkAdjrow.binNo,stkAdjrow.adjQty,stkAdjrow.uom,stkAdjrow.reasonCode,stkAdjrow.transDate,stkAdjrow.reference);
                     sbFileLog.AppendLine("PartNum=" + stkAdjrow.partNo.ToString() + " AdjQty=" + stkAdjrow.adjQty.ToString() + " TranDate=" + stkAdjrow.transDate.ToString("yyyy-MM-dd") + " Reference=" + stkAdjrow.reference.ToString());
                     if (cnt > 0)
                     {
                         //Already exist...
                         sbFileLog.AppendLine("Already Updated...");
                         isSoUpdSuccess = true;
                     }
                     else
                     {
                         
                         dsInvStkAdj = new svcInvQtyAdj.InventoryQtyAdjTableset();                         

                         dsInvStkAdj = svcInvStkAdjClient.GetInventoryQtyAdj(stkAdjrow.partNo, "");

                         dsInvStkAdj.InventoryQtyAdj[0].AdjustQuantity = stkAdjrow.adjQty;
                         dsInvStkAdj.InventoryQtyAdj[0].WareHseCode = stkAdjrow.wareHseCode;
                         dsInvStkAdj.InventoryQtyAdj[0].BinNum = stkAdjrow.binNo;
                         dsInvStkAdj.InventoryQtyAdj[0].ReasonCode = stkAdjrow.reasonCode;
                         //dsInvStkAdj.InventoryQtyAdj[0].ReasonCodeDescription = "Cycle Count Difference";
                         //dsInvStkAdj.InventoryQtyAdj[0].WhseBinDescription = "Default";
                         dsInvStkAdj.InventoryQtyAdj[0].UnitOfMeasure = stkAdjrow.uom;
                         svcInvStkAdjClient.PreSetInventoryQtyAdj(ref dsInvStkAdj);

                         dsInvStkAdj.InventoryQtyAdj[0].RowMod = "U";

                         svcInvStkAdjClient.SetInventoryQtyAdj(ref dsInvStkAdj);

                         //Update StkAdjHistory table
                         oDAL.InsertWMSStockAdjHistory(fileName, stkAdjrow.plant, stkAdjrow.comp, stkAdjrow.partNo, stkAdjrow.wareHseCode, stkAdjrow.binNo, stkAdjrow.adjQty, stkAdjrow.uom, stkAdjrow.reasonCode, stkAdjrow.transDate, stkAdjrow.reference);
                         sbFileLog.AppendLine("Quantity Adjustment Updated...");
                         isSoUpdSuccess = true;
                     }
                 }
             }
             catch (Exception ex)
             {
                 isSoUpdSuccess = false;
                 throw new Exception(ex.Message.ToString());
             }
             finally
             {
                 if (isSoUpdSuccess == false)
                 {
                     sbFileLog.AppendLine("Error Occured...");
                     MoveFile(failedLocation, file);
                     sbFileLog.AppendLine(file + " File Moved to " + failedLocation);
                 }
                 else
                 {
                     MoveFile(successLocation, file);
                     sbFileLog.AppendLine(file + " File Moved to " + successLocation);
                 }
                 if (dsInvStkAdj != null)
                 {
                     dsInvStkAdj = null;
                 }

                 if (oConn != null)
                 {
                     oConn.Dispose();
                     oConn = null;
                 }
             }
        }
        #region FileLock
        public bool IsFileLocked(string file)
        {
            bool Locked = false;
            try
            {
                FileStream fs =
                    File.Open(file, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                fs.Close();
            }
            catch (IOException ex)
            {
                Locked = true;
            }
            return Locked;
        }

        #endregion FIleLock

        public int eventId { get; set; }

        public void MoveFile(string Loc, string file)
        {
            if (!string.IsNullOrEmpty(Loc))
            {
                if (!Directory.Exists(Loc))
                {
                    System.IO.Directory.CreateDirectory(Loc);
                    if (!IsFileLocked(file))
                    {
                        string fileName = Path.GetFileName(file);
                        string dstfile = Path.Combine(Loc, fileName);
                        if (File.Exists(dstfile))
                        {
                            File.Delete(dstfile);

                        }
                        File.Move(file, dstfile);


                    }
                    else
                    {
                        //logInfoBuild.AppendLine("File Locked");
                        //sbLog.AppendLine("File Locked");
                    }
                }
                else
                {
                    if (!IsFileLocked(file))
                    {
                        string fileName = Path.GetFileName(file);
                        string dstfile = Path.Combine(Loc, fileName);
                        if (File.Exists(dstfile))
                        {
                            File.Delete(dstfile);
                        }

                        File.Move(file, dstfile);


                    }
                    else
                    {
                        // logInfoBuild.AppendLine("File Locked");
                        //sbLog.AppendLine("File Locked");
                    }
                }
            }
        }
        public void WriteLogInfoFile(string fileLoc, string file, StringBuilder sbMessage)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            //string WriteLogfileLocation = ConfigurationManager.AppSettings["LogfileLocation"];
            if (!File.Exists(fileLoc + fileName + "_LogInfo.txt"))
                System.IO.File.Create(fileLoc + fileName + "_LogInfo.txt").Dispose();
            using (System.IO.StreamWriter fileWrite =
           new System.IO.StreamWriter(fileLoc + fileName + "_LogInfo.txt", true))
            {
                fileWrite.WriteLine(sbMessage.ToString());
                sbMessage.Clear();
            }

        }
        public void ErrorInfoFile(string location, string errMessage)
        {
            try
            {
                //string fileName = Path.GetFileNameWithoutExtension(file);
                //string WriteLogfileLocation = ConfigurationManager.AppSettings["LogfileLocation"];
                if (!File.Exists(location + "OnStartInfo.txt"))
                    System.IO.File.Create(location + "OnStartInfo.txt").Dispose();
                using (System.IO.StreamWriter fileWrite =
               new System.IO.StreamWriter(location + "OnStartInfo.txt", false))
                {
                    fileWrite.WriteLine(errMessage);
                    errMessage = string.Empty;
                }
            }
            catch(Exception ex)
            {
                FileCreation(ex.Message.ToString());
            }

        }

    }
}
