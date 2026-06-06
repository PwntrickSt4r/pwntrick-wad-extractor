Imports System.IO
Imports System.Text
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

Public Class Form1

    Private Structure WadHeader
        Public Identification As String
        Public NumLumps As Integer
        Public InfoTableOfs As Integer
    End Structure

    Private Structure DirectoryEntry
        Public FilePos As Integer
        Public Size As Integer
        Public Name As String
        Public Index As Integer
    End Structure

    Private currentWadPath As String = ""
    Private lumps As New List(Of DirectoryEntry)()
    Private doomPalette As Color() = Nothing

    Private Const RGB_SIZE As Integer = 3
    Private Const PALETTE_COLORS As Integer = 256

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        SetupListViewColumns()
        btnOpen.Text = "Open WAD"
        btnExtract.Text = "Extract to .png"
        lblInfo.Text = "Open a WAD file to start."
    End Sub

    Private Sub SetupListViewColumns()
        listViewLumps.View = View.Details
        listViewLumps.GridLines = True
        listViewLumps.FullRowSelect = True
        listViewLumps.MultiSelect = True
        listViewLumps.Columns.Clear()
        listViewLumps.Columns.Add("Lump Name", 150)
        listViewLumps.Columns.Add("Size (Bytes)", 120, HorizontalAlignment.Right)
        listViewLumps.Columns.Add("Offset", 120, HorizontalAlignment.Right)
    End Sub

    Private Sub btnOpen_Click(sender As Object, e As EventArgs) Handles btnOpen.Click
        Dim ofd As New OpenFileDialog()
        ofd.Filter = "WAD Files (*.wad)|*.wad"
        If ofd.ShowDialog() = DialogResult.OK Then
            currentWadPath = ofd.FileName
            ParseWadStructure(currentWadPath)
        End If
    End Sub

    Private Sub ParseWadStructure(filePath As String)
        lumps.Clear()
        listViewLumps.Items.Clear()
        doomPalette = Nothing

        Try
            Using fs As New FileStream(filePath, FileMode.Open, FileAccess.Read)
                Using reader As New BinaryReader(fs, Encoding.ASCII)

                    Dim header As New WadHeader()
                    header.Identification = New String(reader.ReadChars(4))
                    If header.Identification <> "IWAD" AndAlso header.Identification <> "PWAD" Then
                        Throw New Exception("Invalid WAD format.")
                    End If
                    header.NumLumps = reader.ReadInt32()
                    header.InfoTableOfs = reader.ReadInt32()

                    lblInfo.Text = String.Format("File: {0} | Total Lumps: {1}", Path.GetFileName(filePath), header.NumLumps)

                    reader.BaseStream.Seek(header.InfoTableOfs, SeekOrigin.Begin)
                    For i As Integer = 0 To header.NumLumps - 1
                        Dim entry As New DirectoryEntry()
                        entry.FilePos = reader.ReadInt32()
                        entry.Size = reader.ReadInt32()
                        entry.Index = i

                        Dim nameBytes As Byte() = reader.ReadBytes(8)
                        Dim rawName As String = Encoding.ASCII.GetString(nameBytes)
                        Dim nullIndex As Integer = rawName.IndexOf(ControlChars.NullChar)
                        entry.Name = If(nullIndex >= 0, rawName.Substring(0, nullIndex), rawName).ToUpper().Trim()

                        lumps.Add(entry)
                    Next

                    Dim playpalEntry = lumps.FirstOrDefault(Function(l) l.Name = "PLAYPAL")
                    If playpalEntry.Size >= (PALETTE_COLORS * RGB_SIZE) Then
                        reader.BaseStream.Seek(playpalEntry.FilePos, SeekOrigin.Begin)
                        doomPalette = New Color(PALETTE_COLORS - 1) {}
                        For i As Integer = 0 To PALETTE_COLORS - 1
                            Dim r As Byte = reader.ReadByte()
                            Dim g As Byte = reader.ReadByte()
                            Dim b As Byte = reader.ReadByte()
                            doomPalette(i) = Color.FromArgb(r, g, b)
                        Next
                    End If

                    listViewLumps.BeginUpdate()
                    For Each lump In lumps
                        Dim item As New ListViewItem(lump.Name)
                        item.SubItems.Add(lump.Size.ToString())
                        item.SubItems.Add(lump.FilePos.ToString())
                        item.Tag = lump
                        listViewLumps.Items.Add(item)
                    Next
                    listViewLumps.EndUpdate()

                End Using
            End Using
        Catch ex As Exception
            MessageBox.Show("Parsing error: " & ex.Message, "Critical Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub btnExtract_Click(sender As Object, e As EventArgs) Handles btnExtract.Click
        If listViewLumps.SelectedItems.Count = 0 Then
            MessageBox.Show("Select one or more lumps from the list to export.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        If doomPalette Is Nothing Then
            MessageBox.Show("Export failed: PLAYPAL lump not found in the current WAD file.", "Palette Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        If FolderBrowserDialog1.ShowDialog() = DialogResult.OK Then
            Dim targetFolder As String = FolderBrowserDialog1.SelectedPath
            Dim exportedCount As Integer = 0
            Dim skippedCount As Integer = 0

            Try
                Using fs As New FileStream(currentWadPath, FileMode.Open, FileAccess.Read)
                    Using reader As New BinaryReader(fs)

                        For Each item As ListViewItem In listViewLumps.SelectedItems
                            Dim entry As DirectoryEntry = DirectCast(item.Tag, DirectoryEntry)

                            If entry.Size = 0 Then
                                skippedCount += 1
                                Continue For
                            End If

                            Dim bmp As Bitmap = DecodePatch(reader, entry)

                            If bmp IsNot Nothing Then
                                Dim safeName As String = entry.Name
                                For Each invalidChar In Path.GetInvalidFileNameChars()
                                    safeName = safeName.Replace(invalidChar, "_"c)
                                Next

                                Dim outputPath As String = Path.Combine(targetFolder, safeName & ".png")

                                bmp.Save(outputPath, ImageFormat.Png)
                                bmp.Dispose()
                                exportedCount += 1
                            Else
                                skippedCount += 1
                            End If
                        Next

                    End Using
                End Using

                MessageBox.Show(String.Format("Export completed successfully!{0}Successfully converted: {1}{0}Skipped (non-graphic data): {2}", _
                                              Environment.NewLine, exportedCount, skippedCount), _
                                "Operation Result", MessageBoxButtons.OK, MessageBoxIcon.Information)

            Catch ex As Exception
                MessageBox.Show("Error exporting data: " & ex.Message, "I/O Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End If
    End Sub

    Private Function DecodePatch(reader As BinaryReader, entry As DirectoryEntry) As Bitmap
        Dim startPosition As Long = reader.BaseStream.Position
        reader.BaseStream.Seek(entry.FilePos, SeekOrigin.Begin)

        Try
            Dim width As Int16 = reader.ReadInt16()
            Dim height As Int16 = reader.ReadInt16()

            If width <= 0 OrElse height <= 0 OrElse width > 2048 OrElse height > 2048 Then Return Nothing

            Dim leftOfs As Int16 = reader.ReadInt16()
            Dim topOfs As Int16 = reader.ReadInt16()

            Dim colPointers(width - 1) As Int32
            For x As Integer = 0 To width - 1
                colPointers(x) = reader.ReadInt32()
                If colPointers(x) >= entry.Size Then Return Nothing
            Next

            Dim bmp As New Bitmap(width, height, PixelFormat.Format32bppArgb)
            Dim bmpData As BitmapData = bmp.LockBits(New Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bmp.PixelFormat)
            Dim numBytes As Integer = bmpData.Stride * height
            Dim rgbValues(numBytes - 1) As Byte

            For x As Integer = 0 To width - 1
                reader.BaseStream.Seek(entry.FilePos + colPointers(x), SeekOrigin.Begin)

                While True
                    Dim rowStart As Byte = reader.ReadByte()
                    If rowStart = 255 Then Exit While

                    Dim pixelCount As Byte = reader.ReadByte()
                    reader.ReadByte()

                    For i As Integer = 0 To pixelCount - 1
                        Dim colorIndex As Byte = reader.ReadByte()
                        Dim y As Integer = rowStart + i

                        If y < height Then
                            Dim palColor As Color = doomPalette(colorIndex)
                            Dim byteIndex As Integer = (y * bmpData.Stride) + (x * 4)
                            rgbValues(byteIndex) = palColor.B
                            rgbValues(byteIndex + 1) = palColor.G
                            rgbValues(byteIndex + 2) = palColor.R
                            rgbValues(byteIndex + 3) = 255
                        End If
                    Next
                    reader.ReadByte()
                End While
            Next

            Marshal.Copy(rgbValues, 0, bmpData.Scan0, numBytes)
            bmp.UnlockBits(bmpData)
            Return bmp

        Catch
            Return Nothing
        Finally
            reader.BaseStream.Seek(startPosition, SeekOrigin.Begin)
        End Try
    End Function

End Class