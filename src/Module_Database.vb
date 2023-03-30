﻿Imports System.IO
Imports System.Data.SQLite
Imports ScintillaNET
Imports TmCGPTD.Form_Input
Imports System.Text.Json
Imports CsvHelper
Imports CsvHelper.Configuration
Imports System.Globalization
Imports System.ComponentModel

Partial Public Class Form1
    ' SQログLテーブル初期化--------------------------------------------------------------
    Private Sub CreateDatabase()
        Using connection As New SQLiteConnection($"Data Source={dbPath}")
            Dim sql As String = "CREATE TABLE log (id INTEGER PRIMARY KEY AUTOINCREMENT, date DATE, text TEXT NOT NULL DEFAULT '');"
            Using command As New SQLiteCommand(sql, connection)
                ' テーブル作成
                connection.Open()
                command.ExecuteNonQuery()

                ' インデックス作成
                sql = "CREATE INDEX idx_text ON log (text);"
                command.CommandText = sql
                command.ExecuteNonQuery()

                connection.Close()
            End Using
        End Using
    End Sub

    ' SQLチャットログテーブルチェック--------------------------------------------------------------
    Public Function TableExists(ByVal tableName As String) As Boolean
        Dim exists As Boolean = False
        Using connection As New SQLiteConnection($"Data Source={dbPath}")
            connection.Open()
            Using command As New SQLiteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@tableName;", connection)
                command.Parameters.AddWithValue("@tableName", tableName)
                Dim count As Integer = Convert.ToInt32(command.ExecuteScalar())
                If count > 0 Then
                    exists = True
                End If
            End Using
            connection.Close()
        End Using
        Return exists
    End Function

    ' SQLチャットログテーブル初期化--------------------------------------------------------------
    Private Sub CreateDatabaseChat()
        Using connection As New SQLiteConnection($"Data Source={dbPath}")
            Dim sql As String = "CREATE TABLE chatlog (id INTEGER PRIMARY KEY AUTOINCREMENT, date DATE, title TEXT NOT NULL DEFAULT '', tag TEXT NOT NULL DEFAULT '', json TEXT NOT NULL DEFAULT '', text TEXT NOT NULL DEFAULT '');"
            Using command As New SQLiteCommand(sql, connection)
                ' テーブルを作成
                connection.Open()
                command.ExecuteNonQuery()

                ' インデックス作成
                sql = "CREATE INDEX idx_chat_text ON chatlog (text);"
                command.CommandText = sql
                command.ExecuteNonQuery()

                connection.Close()
            End Using
        End Using
    End Sub

    ' SQL dbファイルをメモリにロード--------------------------------------------------------------
    Public Sub DbLoadToMemory()
        Dim fileConnection As New SQLiteConnection($"Data Source={dbPath}")
        fileConnection.Open()
        ' メモリ上のDBファイルを作成
        memoryConnection = New SQLiteConnection("Data Source=:memory:")
        memoryConnection.Open()
        Try
            ' SQL dbをメモリにコピー
            fileConnection.BackupDatabase(memoryConnection, "main", "main", -1, Nothing, 0)
        Catch ex As Exception
            MsgBox($"Error : {ex.Message}")
        End Try
        fileConnection.Close()
    End Sub

    ' ローカルにデータベースファイルをバックアップ（未使用）--------------------------------------------------------------
    Public Sub BackupSqlDb()

        File.Copy(dbPath, dbBuPath, True)
        ' Task.Delay(500)
        ' ローカルファイルにバックアップ
        Using backupConnection As New SQLiteConnection($"Data Source={dbBuPath}")
            backupConnection.Open()
            memoryConnection.BackupDatabase(backupConnection, "main", "main", -1, Nothing, 0)
        End Using

        Try
            ' バックアップファイルを前のDBに上書き
            File.Move(dbBuPath, dbPath, True)
            File.Delete(dbBuPath)
        Catch ex As Exception
            MsgBox($"Error : {ex.Message}")
        End Try
        ' いったん閉じてまた開く
        memoryConnection.Close()
        DbLoadToMemory()
    End Sub

    ' データベースにインサートする--------------------------------------------------------------
    Public Async Function InsertDatabaseAsync() As Task
        If Not String.IsNullOrEmpty(recentText) Then

            inputText.Clear()
            Await Task.Run(Sub() inputForm.TextInput1.Invoke(Sub() inputText.Add(String.Join(Environment.NewLine, inputForm.TextInput1.Text))))
            Await Task.Run(Sub() inputForm.TextInput2.Invoke(Sub() inputText.Add(String.Join(Environment.NewLine, inputForm.TextInput2.Text))))
            Await Task.Run(Sub() inputForm.TextInput3.Invoke(Sub() inputText.Add(String.Join(Environment.NewLine, inputForm.TextInput3.Text))))
            Await Task.Run(Sub() inputForm.TextInput4.Invoke(Sub() inputText.Add(String.Join(Environment.NewLine, inputForm.TextInput4.Text))))
            Await Task.Run(Sub() inputForm.TextInput5.Invoke(Sub() inputText.Add(String.Join(Environment.NewLine, inputForm.TextInput5.Text))))
            Dim finalText = String.Join(Environment.NewLine & "<---TMCGPT--->" & Environment.NewLine, inputText)

            ' 現在時刻を取得する
            Dim now As DateTime = DateTime.Now

            ' データベースに接続する
            Using connection As New SQLiteConnection($"Data Source={dbPath}")
                connection.Open()

                ' トランザクションを開始する
                Using transaction As SQLiteTransaction = connection.BeginTransaction()
                    Try
                        ' logテーブルにデータをインサートする
                        Using command As New SQLiteCommand("INSERT INTO log(date, text) VALUES (@date, @text)", connection)
                            command.Parameters.AddWithValue("@date", now)
                            command.Parameters.AddWithValue("@text", finalText)
                            Await command.ExecuteNonQueryAsync()
                        End Using

                        ' トランザクションをコミットする
                        Await Task.Run(Sub() transaction.Commit())
                    Catch ex As Exception
                        ' エラーが発生した場合、トランザクションをロールバックする
                        transaction.Rollback()
                        MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    End Try
                End Using
            End Using
        End If
        ' インメモリをいったん閉じてまた開く
        memoryConnection.Close()
        DbLoadToMemory()
    End Function
    ' チャットログを更新--------------------------------------------------------------
    Public lastRowId As Int64 = Nothing ' チャットログ書き込み先IDを保持
    Public Async Function InsertDatabaseChatAsync(postDate As Date, resDate As Date, resText As String) As Task
        If Not String.IsNullOrEmpty(recentText) Then
            Dim insertText As New List(Of String)
            insertText.Add($"[{postDate.ToString}] by You" & Environment.NewLine)
            insertText.Add(recentText & Environment.NewLine)
            insertText.Add($"[{resDate.ToString}] by ChatGPT")
            insertText.Add(resText)

            Dim titleText As String = String.Empty ' chatForm.TextBoxTitleのテキストを取得しておく
            If chatForm.TextBoxTitle.InvokeRequired Then
                Await Task.Run(Sub() chatForm.TextBoxTitle.BeginInvoke(Sub() titleText = chatForm.TextBoxTitle.Text))
            Else
                titleText = chatForm.TextBoxTitle.Text
            End If

            Dim insertTag As New List(Of String)
            Await Task.Run(Sub() chatForm.TextBoxTag1.Invoke(Sub() insertTag.Add(chatForm.TextBoxTag1.Text)))
            Await Task.Run(Sub() chatForm.TextBoxTag2.Invoke(Sub() insertTag.Add(chatForm.TextBoxTag2.Text)))
            Await Task.Run(Sub() chatForm.TextBoxTag3.Invoke(Sub() insertTag.Add(chatForm.TextBoxTag3.Text)))
            'insertTag.RemoveAll(Function(s) String.IsNullOrEmpty(s))
            Dim insertTagStr As String = String.Join(Environment.NewLine, insertTag)

            Dim jsonConversationHistory As String = JsonSerializer.Serialize(conversationHistory)

            Using connection As New SQLiteConnection($"Data Source={dbPath}")
                connection.Open()
                ' トランザクションを開始する
                Using transaction As SQLiteTransaction = connection.BeginTransaction()
                    Try
                        If lastRowId <> Nothing Then
                            ' 指定されたIDのデータを取得する
                            Dim currentText As String = ""
                            Using command As New SQLiteCommand("SELECT text FROM chatlog WHERE id=@id", connection)
                                command.Parameters.AddWithValue("@id", lastRowId)
                                Using reader As SQLiteDataReader = CType(Await command.ExecuteReaderAsync(), SQLiteDataReader)
                                    If reader.Read() Then
                                        currentText = reader.GetString(0)
                                    End If
                                End Using
                            End Using

                            ' 既存のテキストに新しいメッセージを追加する
                            Dim newText As String = currentText + Environment.NewLine + String.Join(Environment.NewLine, insertText)

                            ' 指定されたIDに対してデータを更新する
                            Using command As New SQLiteCommand("UPDATE chatlog SET date=@date, title=@title, tag=@tag, json=@json, text=@text WHERE id=@id", connection)
                                Await Task.Run(Sub() command.Parameters.AddWithValue("@date", resDate))
                                Await Task.Run(Sub() command.Parameters.AddWithValue("@title", titleText))
                                Await Task.Run(Sub() command.Parameters.AddWithValue("@tag", insertTagStr))
                                Await Task.Run(Sub() command.Parameters.AddWithValue("@json", jsonConversationHistory))
                                Await Task.Run(Sub() command.Parameters.AddWithValue("@text", newText))
                                Await Task.Run(Sub() command.Parameters.AddWithValue("@id", lastRowId))
                                Await command.ExecuteNonQueryAsync()
                            End Using
                        Else
                            ' logテーブルにデータをインサートする
                            Using command As New SQLiteCommand("INSERT INTO chatlog(date, title, tag, json, text) VALUES (@date, @title, @tag, @json, @text)", connection)
                                Await Task.Run(Sub() command.Parameters.AddWithValue("@date", resDate))
                                Await Task.Run(Sub() command.Parameters.AddWithValue("@title", titleText))
                                Await Task.Run(Sub() command.Parameters.AddWithValue("@tag", insertTagStr))
                                Await Task.Run(Sub() command.Parameters.AddWithValue("@json", jsonConversationHistory))
                                Await Task.Run(Sub() command.Parameters.AddWithValue("@text", String.Join(Environment.NewLine, insertText)))
                                Await command.ExecuteNonQueryAsync()
                            End Using

                            ' 更新中チャットのIDを取得
                            Dim sqlLastRowId As String = "SELECT last_insert_rowid();"
                            Using command As New SQLiteCommand(sqlLastRowId, connection)
                                lastRowId = CLng(command.ExecuteScalar())
                            End Using
                        End If
                        ' トランザクションをコミットする
                        Await Task.Run(Sub() transaction.Commit())
                    Catch ex As Exception
                        ' エラーが発生した場合、トランザクションをロールバックする
                        transaction.Rollback()
                        MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    End Try
                End Using

            End Using
        End If
        ' インメモリをいったん閉じてまた開く
        memoryConnection.Close()
        DbLoadToMemory()
    End Function

    ' データベースからEditorログを検索--------------------------------------------------------------
    Public Async Function SearchDatabaseAsync(query As String) As Task
        Using cmd As New SQLiteCommand(query, memoryConnection)
            Using reader As SQLiteDataReader = CType(Await cmd.ExecuteReaderAsync(), SQLiteDataReader)
                ' 検索結果を整形してリストに追加
                Dim dropList As New List(Of String)
                Dim allRecords As New List(Of String)
                While Await reader.ReadAsync()
                    Dim id As String = reader.GetInt32(0).ToString
                    Dim dateStr As String = reader.GetDateTime(1).ToString("yyyy/MM/dd HH:mm:ss")
                    Dim text As String = reader.GetString(2).Replace(vbCrLf, " ").Replace(vbCr, " ").Replace(vbLf, " ")

                    text = text.Replace("<---TMCGPT--->", "-")
                    text = If(text.Length > 80, text.Substring(0, 80) & "…", text)

                    allRecords.Add($"{dateStr}  {text} #{id}")
                    dropList.Add($"{dateStr}  {text} #{id}")
                End While

                ' 検索結果が空なら、"No matching rows found."をリストに追加
                If dropList.Count = 0 Then dropList.Add("No matching results.")

                ' ドロップダウンリストに設定
                Await Task.Run(Sub() inputForm.ComboBoxSearch.BeginInvoke(Sub() inputForm.ComboBoxSearch.Items.AddRange(dropList.ToArray())))
            End Using
        End Using
    End Function

    'タイトルとタグの更新--------------------------------------------------------------
    Public Async Function UpdateDatabaseAsync(query As String) As Task
        Try
            Using connection As New SQLiteConnection($"Data Source={dbPath};Version=3;")
                Await connection.OpenAsync()

                Using command As New SQLiteCommand(query, connection)
                    Await command.ExecuteNonQueryAsync()
                End Using
            End Using

        Catch ex As Exception
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        ' インメモリをいったん閉じてまた開く
        memoryConnection.Close()
        DbLoadToMemory()
    End Function

    ' データベースから表示用タグリストを取得--------------------------------------------------------------
    Async Function GetTagDatabaseAsync() As Task(Of HashSet(Of String))
        ' タグを格納するHashSet
        Dim uniqueTags As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        uniqueTags.Add("(All)")
        Dim query As String = "SELECT tag FROM chatlog"
        Using command As New SQLiteCommand(query, memoryConnection)
            Using reader As SQLiteDataReader = CType(Await command.ExecuteReaderAsync(), SQLiteDataReader)
                While Await reader.ReadAsync()
                    ' タグを改行で分割
                    Dim tags As String() = reader("tag").ToString().Split(New String() {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)

                    ' 重複しないタグをHashSetに追加
                    For Each tag As String In tags
                        uniqueTags.Add(tag.Trim())
                    Next
                End While
            End Using
        End Using
        Return uniqueTags
    End Function

    ' データベースから表示用チャットログを取得--------------------------------------------------------------
    Public Async Function PickChatDatabaseAsync(query As String) As Task(Of List(Of String))
        Dim result As New List(Of String)
        Using cmd As New SQLiteCommand(query, memoryConnection)
            Using reader As SQLiteDataReader = CType(Await cmd.ExecuteReaderAsync(), SQLiteDataReader)
                If Await reader.ReadAsync() Then
                    result.Add(reader.GetString(0))
                    result.Add(reader.GetString(1))
                    result.Add(reader.GetString(2))
                    result.Add(reader.GetString(3))
                End If
            End Using
        End Using
        Return result
    End Function
    ' データベースからチャットログを検索--------------------------------------------------------------
    Public Async Function SearchChatDatabaseAsync(query As String, Optional ByVal searchKey As String = "") As Task(Of DataTable)
        Dim dT As New DataTable
        Using cmd As New SQLiteCommand(query, memoryConnection)
            If Not searchKey = "" Then
                searchKey = "%" & searchKey.ToLower() & "%"
                cmd.Parameters.AddWithValue("@searchKey", searchKey)
            End If
            Using reader As SQLiteDataReader = CType(Await cmd.ExecuteReaderAsync(), SQLiteDataReader)
                ' DataTableのデータ型を明示設定（処理速度を考慮）
                dT.Columns.Add("chatId", GetType(Integer))
                dT.Columns.Add("chatDate", GetType(Date))
                dT.Columns.Add("chatTitle", GetType(String))
                dT.Columns.Add("chatTag", GetType(String))
                Dim isFirstLine As Boolean = True
                While Await reader.ReadAsync()
                    Dim row As DataRow = dT.NewRow()
                    If isFirstLine Then
                        lastRowId = reader.GetInt32(0)
                        row("chatId") = lastRowId
                        isFirstLine = False
                    Else
                        row("chatId") = reader.GetInt32(0)
                    End If
                    row("chatDate") = reader.GetDateTime(1)
                    row("chatTitle") = reader.GetString(2)
                    row("chatTag") = reader.GetString(3).Replace(Environment.NewLine, ", ")
                    dT.Rows.Add(row)
                End While
            End Using
        End Using
        Return dT
    End Function

    ' データベースからEditor日付ログを検索--------------------------------------------------------------
    Public Sub SearchDatabaseByDate(query As String, searchDate As DateTime)
        Using cmd As New SQLiteCommand(query, memoryConnection)
            cmd.Parameters.AddWithValue("@searchDate", searchDate)
            Using reader As SQLiteDataReader = cmd.ExecuteReader()
                ' 検索結果を整形してリストに追加
                Dim dropList As New List(Of String)
                If reader.Read() Then
                    Dim id As String = reader.GetInt32(0).ToString
                    Dim dateStr As String = reader.GetDateTime(1).ToString("yyyy/MM/dd HH:mm:ss")
                    Dim text As String = reader.GetString(2).Replace(vbCrLf, " ").Replace(vbCr, " ").Replace(vbLf, " ")

                    text = text.Replace("<---TMCGPT--->", "-")
                    text = If(text.Length > 28, text.Substring(0, 28) & "…", text)
                    dropList.Add($"{dateStr}  {text} #{id}")
                    inputForm.ComboBoxSearch.Text = $"{dateStr}  {text} #{id}"

                    ' textカラムの値を取得して、区切り文字「---」で分割する
                    Dim texts As String()
                    Dim textStr As String = reader.GetString(2)
                    texts = textStr.Split({"<---TMCGPT--->"}, StringSplitOptions.None)
                    For i As Integer = 0 To Math.Min(texts.Length - 1, 4) ' 5要素目までを取得
                        Dim inputBox As Scintilla = CType(inputForm.Controls.Find($"TextInput{i + 1}", True)(0), Scintilla)
                        inputBox.Text = ""
                        If Not String.IsNullOrWhiteSpace(texts(i)) Then
                            inputBox.Text = texts(i).Trim() ' 空白を削除して反映
                        End If
                    Next
                Else
                    MsgBox("There is no more data.")
                    Return
                End If
                inputForm.ComboBoxSearch.DroppedDown = False ' ドロップダウンリストを表示しない
                inputForm.ComboBoxSearch.Items.AddRange(dropList.ToArray()) ' ドロップダウンリストに設定

            End Using
        End Using
    End Sub

    ' CSVエキスポート--------------------------------------------------------------
    Public Async Function ExportTableToCsvAsync(tableName As String) As Task
        Try
            ' ファイル保存ダイアログを表示
            Dim saveFileDialog As New SaveFileDialog()
            saveFileDialog.InitialDirectory = Application.ExecutablePath
            saveFileDialog.Filter = $"CSV Files (*.csv)|{tableName}.csv"
            If saveFileDialog.ShowDialog() <> DialogResult.OK Then
                Return
            End If
            Dim fileName As String = saveFileDialog.FileName

            ProgressDialog.Show()

            Dim processedCount As Integer = 0

            ' SELECT クエリを実行し、テーブルのデータを取得
            Dim command As New SQLiteCommand($"SELECT * FROM {tableName};", memoryConnection)
            Using reader As SQLiteDataReader = CType(Await command.ExecuteReaderAsync(), SQLiteDataReader)

                ' CSV ファイルに書き込むための StreamWriter を作成
                Using writer As New StreamWriter(fileName, False, System.Text.Encoding.UTF8)

                    ' CsvWriter を作成し、設定を適用
                    Dim config As New CsvConfiguration(CultureInfo.InvariantCulture) With {
                    .HasHeaderRecord = True,
                    .Delimiter = ","
                }
                    Using csvWriter As New CsvWriter(writer, config)

                        Dim commandRowCount As New SQLiteCommand($"SELECT COUNT(*) FROM {tableName};", memoryConnection)
                        Dim rowCount As Integer = CInt(commandRowCount.ExecuteScalar())

                        ' ヘッダー行を書き込む
                        For i As Integer = 0 To reader.FieldCount - 1
                            csvWriter.WriteField(reader.GetName(i))
                        Next
                        csvWriter.NextRecord()

                        ' データ行を書き込む

                        While Await reader.ReadAsync()
                            For i As Integer = 0 To reader.FieldCount - 1
                                If reader.GetFieldType(i) = GetType(DateTime) Then
                                    Dim dateValue As DateTime = reader.GetDateTime(i)
                                    csvWriter.WriteField(dateValue.ToString("yyyy-MM-dd HH:mm:ss.fffffff"))
                                Else
                                    csvWriter.WriteField(reader.GetValue(i))
                                End If
                            Next
                            csvWriter.NextRecord()
                            ' Report progress
                            processedCount += 1
                            Dim progressPercentage As Integer = CInt((processedCount / rowCount) * 100)
                            ProgressDialog.Invoke(New Action(Sub()
                                                                 ProgressDialog.UpdateProgress(progressPercentage)
                                                             End Sub))
                        End While
                    End Using
                End Using
            End Using
            ProgressDialog.Invoke(New Action(Sub()
                                                 ProgressDialog.Close()
                                             End Sub))
            MsgBox($"Successfully exported log. ({processedCount} Records)", MsgBoxStyle.Information, "Information")
        Catch ex As Exception
            MsgBox("Failed to export log." & vbCrLf & "Error: " & ex.Message, MsgBoxStyle.Exclamation, "Error")
        End Try
    End Function


    ' CSVインポート--------------------------------------------------------------
    Public Async Function ImportCsvToTableAsync(tableName As String) As Task
        ' ファイルオープンダイアログを表示
        Dim openFileDialog As New OpenFileDialog()
        openFileDialog.InitialDirectory = Application.ExecutablePath
        openFileDialog.Filter = "CSV Files (*.csv)|*.csv"
        If openFileDialog.ShowDialog() <> DialogResult.OK Then
            Return
        End If
        Dim processedCount As Integer = 0
        Dim fileName As String = openFileDialog.FileName
        Dim columnEnd As Integer
        Dim columnNames As String
        If tableName = "log" Then
            columnEnd = 2
            columnNames = "date, text"
        Else
            columnEnd = 5
            columnNames = "date, title, tag, json, text"
        End If

        ' CSVファイルからデータを読み込む
        Using reader As New StreamReader(fileName, System.Text.Encoding.UTF8)
            Dim config As New CsvConfiguration(CultureInfo.InvariantCulture) With {
            .HasHeaderRecord = True,
            .Delimiter = ","
        }
            Using csvReader As New CsvReader(reader, config)
                csvReader.Read() ' ヘッダー行をスキップ

                Using con As New SQLiteConnection($"Data Source={dbPath}")
                    Await con.OpenAsync()
                    Using transaction As SQLiteTransaction = con.BeginTransaction()
                        Try
                            While Await csvReader.ReadAsync() ' データ行を読み込む

                                ' データを取得
                                Dim rowData As New List(Of String)()
                                For i As Integer = 1 To columnEnd ' 2列目から6列目まで
                                    rowData.Add(csvReader.GetField(i))
                                Next
                                ' INSERT文を作成
                                Dim values As String = String.Join(", ", Enumerable.Range(0, rowData.Count).Select(Function(i) $"@value{i}"))

                                Dim insertQuery As String = $"INSERT INTO {tableName} ({columnNames}) VALUES ({values});"

                                ' データをデータベースに挿入
                                Using command As New SQLiteCommand(insertQuery, con)
                                    For i As Integer = 0 To rowData.Count - 1
                                        command.Parameters.AddWithValue($"@value{i}", rowData(i))
                                    Next
                                    Await command.ExecuteNonQueryAsync()
                                End Using
                                processedCount += 1
                            End While

                            transaction.Commit()
                            MsgBox($"Successfully imported log. ({processedCount} Records)", MsgBoxStyle.Information, "Information")
                        Catch ex As Exception
                            transaction.Rollback()
                            Throw
                            MsgBox("Failed to import log." & vbCrLf & "Error: " & ex.Message, MsgBoxStyle.Exclamation, "Error")
                        End Try
                    End Using
                    Await con.CloseAsync()
                End Using
            End Using
        End Using

        ' インメモリをいったん閉じてまた開く
        memoryConnection.Close()
        DbLoadToMemory()
    End Function

End Class

Public Class ProgressDialog
    Inherits Form

    Public Sub New()
        ' Initialize components and set properties
        InitializeComponent()

        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.ControlBox = False
        Me.ShowInTaskbar = False
        Me.MinimizeBox = False
        Me.MaximizeBox = False
        Me.Text = "Exporting to CSV"
    End Sub

    Public Sub UpdateProgress(value As Integer)
        ' Update the progress bar value
        Me.ProgressBar1.Value = value
    End Sub

    Private Sub InitializeComponent()
        Me.ProgressBar1 = New ProgressBar()
        Me.SuspendLayout()
        '
        'ProgressBar1
        '
        Me.ProgressBar1.Location = New System.Drawing.Point(12, 12)
        Me.ProgressBar1.Name = "ProgressBar1"
        Me.ProgressBar1.Size = New System.Drawing.Size(260, 23)
        Me.ProgressBar1.TabIndex = 0
        '
        'ProgressDialog
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(284, 61)
        Me.Controls.Add(Me.ProgressBar1)
        Me.Name = "ProgressDialog"
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents ProgressBar1 As ProgressBar
End Class