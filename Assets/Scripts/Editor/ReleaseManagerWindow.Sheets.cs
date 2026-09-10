using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

public partial class ReleaseManagerWindow
{
    string sheetsTarget = string.Empty;
    List<string> sheetsFiles = new();
    readonly HashSet<string> sheetsSelected = new(StringComparer.Ordinal);
    List<string> sheetsPreviewFiles;
    SpecSheetsUploadPlan sheetsPlan;
    string sheetsReport;
    string sheetsError;
    bool sheetsBusy;
    string sheetsOperation;
    CancellationTokenSource sheetsCancellation;
    Vector2 sheetsScroll;
    Vector2 sheetsFilesScroll;
    Vector2 sheetsReportScroll;
    GUIStyle sheetsReportStyle;

    void EnableSheetsTab()
    {
        this.sheetsTarget = SpecSheetsUploader.ReadConfiguredSpreadsheetId() ?? string.Empty;
        this.sheetsSelected.Clear();
        ReloadSheetsFiles();
    }

    void DisableSheetsTab()
    {
        this.sheetsCancellation?.Cancel();
        this.sheetsCancellation = null;
        this.sheetsBusy = false;
        this.sheetsPlan = null;
    }

    void DrawSheetsTab()
    {
        this.sheetsScroll = EditorGUILayout.BeginScrollView(this.sheetsScroll);
        Header("CSV → Google Sheet");
        EditorGUILayout.HelpBox(
            "docs/SpecData/*_sheet.csv를 선택해 Google Sheet에 반영한다. 먼저 대상 문서와 변경 내용을 미리 확인한다.\n" +
            "저장소의 진실원은 CSV다. 이 작업은 Firestore에 배포하거나 SpecData.bytes·CS를 생성하지 않는다.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(this.sheetsBusy))
        {
            DrawSheetsConnection();
            EditorGUILayout.Space(6);
            string t_target = EditorGUILayout.TextField("문서 URL 또는 ID", this.sheetsTarget);
            if (t_target != this.sheetsTarget)
            {
                this.sheetsTarget = t_target;
                InvalidateSheetsPreview();
            }

            DrawSheetsFileSelection();

            using (new EditorGUI.DisabledScope(!SpecGoogleSheetsAuth.IsSignedIn
                || this.sheetsSelected.Count == 0 || string.IsNullOrWhiteSpace(this.sheetsTarget)))
            {
                if (GUILayout.Button("변경 내용 미리보기", GUILayout.Height(28))) PreviewSheetsAsync();
            }

            bool t_canUpload = this.sheetsPlan != null && this.sheetsPlan.CanUpload
                && this.sheetsPlan.ChangedCellCount > 0 && SpecGoogleSheetsAuth.IsSignedIn;
            using (new EditorGUI.DisabledScope(!t_canUpload))
            {
                if (GUILayout.Button("미리본 변경 업로드", GUILayout.Height(28))) UploadSheetsAsync();
            }
        }

        if (this.sheetsBusy)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool t_cancelling = this.sheetsCancellation != null && this.sheetsCancellation.IsCancellationRequested;
                EditorGUILayout.LabelField(t_cancelling ? "취소 요청 중…" : this.sheetsOperation + " 중…");
                using (new EditorGUI.DisabledScope(t_cancelling))
                {
                    if (GUILayout.Button("취소", GUILayout.Width(70))) this.sheetsCancellation?.Cancel();
                }
            }
        }

        if (!string.IsNullOrEmpty(this.sheetsError))
            EditorGUILayout.HelpBox(this.sheetsError, MessageType.Error);
        if (this.sheetsPlan != null)
        {
            EditorGUILayout.LabelField("대상 문서", this.sheetsPlan.SpreadsheetTitle);
            EditorGUILayout.LabelField("변경 셀", this.sheetsPlan.ChangedCellCount.ToString());
            if (this.sheetsPlan.ChangedCellCount == 0)
                EditorGUILayout.HelpBox("반영할 셀 변경이 없다.", MessageType.Info);
            else if (!this.sheetsPlan.CanUpload)
                EditorGUILayout.HelpBox("업로드할 수 없다. 아래 미리보기의 차단 사유를 확인한다.", MessageType.Warning);
        }

        DrawSheetsReport();
        EditorGUILayout.EndScrollView();
    }

    void DrawSheetsConnection()
    {
        EditorGUILayout.LabelField("Google Sheets", SpecGoogleSheetsAuth.IsSignedIn ? "연결됨" : "연결 안 됨");
        EditorGUILayout.HelpBox(
            "대상 문서를 편집할 수 있는 Google 계정으로 연결한다. Google Cloud 프로젝트에서 Google Sheets API를 사용 설정하고, " +
            "로그인 탭의 '구글 OAuth 클라이언트 설정'에 데스크톱 앱 클라이언트 ID·보안 비밀을 입력한다. " +
            "OAuth 앱이 테스트 상태라면 연결할 계정을 테스트 사용자로 등록한다.",
            MessageType.None);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (SpecGoogleSheetsAuth.IsSignedIn)
            {
                if (GUILayout.Button("Sheets 연결 해제"))
                {
                    SpecGoogleSheetsAuth.SignOut();
                    InvalidateSheetsPreview();
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(!GoogleOAuthSignIn.IsConfigured))
                {
                    if (GUILayout.Button("Google Sheets 연결"))
                    {
                        InvalidateSheetsPreview();
                        try
                        {
                            if (!SpecGoogleSheetsAuth.TrySignIn(out string t_error)) this.sheetsError = t_error;
                        }
                        catch (Exception)
                        {
                            this.sheetsError = "Google Sheets 연결에 실패했다. OAuth 설정을 확인하고 다시 연결한다.";
                        }
                    }
                }
            }
            if (GUILayout.Button("로그인 탭에서 OAuth 설정", GUILayout.Width(180)))
            {
                this.adminOAuthOpen = true;
                this.selectedTab = Tab.Auth;
            }
        }
    }

    void DrawSheetsFileSelection()
    {
        Header($"CSV 선택 ({this.sheetsSelected.Count}/{this.sheetsFiles.Count})");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("목록 다시 읽기")) ReloadSheetsFiles();
            if (GUILayout.Button("전체 선택"))
            {
                foreach (string t_file in this.sheetsFiles) this.sheetsSelected.Add(t_file);
                InvalidateSheetsPreview();
            }
            if (GUILayout.Button("전체 해제"))
            {
                this.sheetsSelected.Clear();
                InvalidateSheetsPreview();
            }
        }

        if (this.sheetsFiles.Count == 0)
            EditorGUILayout.HelpBox("docs/SpecData에서 *_sheet.csv를 찾지 못했다.", MessageType.Info);
        else
        {
            this.sheetsFilesScroll = EditorGUILayout.BeginScrollView(this.sheetsFilesScroll, GUILayout.Height(180));
            foreach (string t_file in this.sheetsFiles)
            {
                bool t_was = this.sheetsSelected.Contains(t_file);
                bool t_now = EditorGUILayout.ToggleLeft(Path.GetFileName(t_file), t_was);
                if (t_now == t_was) continue;
                if (t_now) this.sheetsSelected.Add(t_file);
                else this.sheetsSelected.Remove(t_file);
                InvalidateSheetsPreview();
            }
            EditorGUILayout.EndScrollView();
        }
    }

    void ReloadSheetsFiles()
    {
        InvalidateSheetsPreview();
        try
        {
            this.sheetsFiles = SpecSheetsUploader.GetCsvFiles();
            this.sheetsFiles.Sort(StringComparer.Ordinal);
            this.sheetsSelected.IntersectWith(this.sheetsFiles);
        }
        catch (Exception)
        {
            this.sheetsFiles.Clear();
            this.sheetsSelected.Clear();
            this.sheetsError = "CSV 목록을 읽지 못했다. docs/SpecData 경로와 파일 접근 권한을 확인한다.";
        }
    }

    void InvalidateSheetsPreview()
    {
        this.sheetsPlan = null;
        this.sheetsPreviewFiles = null;
        this.sheetsReport = null;
        this.sheetsError = null;
    }

    async void PreviewSheetsAsync()
    {
        if (this.sheetsBusy) return;
        InvalidateSheetsPreview();
        CancellationTokenSource t_cancel = BeginSheetsOperation("미리보기");
        try
        {
            string t_id = SpecSheetsUploader.NormalizeSpreadsheetId(this.sheetsTarget);
            if (!SpecGoogleSheetsAuth.TryGetAccessToken(out string t_token, out string t_error))
            {
                this.sheetsError = t_error;
                return;
            }
            var t_files = new List<string>(this.sheetsSelected);
            t_files.Sort(StringComparer.Ordinal);
            SpecSheetsUploadPlan t_plan = await SpecSheetsUploader.PreviewAsync(t_id, t_files, t_token, t_cancel.Token);
            t_cancel.Token.ThrowIfCancellationRequested();
            if (!IsCurrentSheetsOperation(t_cancel)) return;
            this.sheetsPlan = t_plan;
            this.sheetsPreviewFiles = t_files;
            this.sheetsReport = t_plan.Report;
            this.sheetsReportScroll = Vector2.zero;
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentSheetsOperation(t_cancel)) this.sheetsReport = "미리보기를 취소했다.";
        }
        catch (InvalidOperationException t_exception)
        {
            if (IsCurrentSheetsOperation(t_cancel)) this.sheetsError = t_exception.Message;
        }
        catch (Exception)
        {
            if (IsCurrentSheetsOperation(t_cancel)) this.sheetsError = "미리보기에 실패했다. 문서 URL/ID, 연결 상태와 문서 편집 권한을 확인한다.";
        }
        finally { EndSheetsOperation(t_cancel); }
    }

    async void UploadSheetsAsync()
    {
        SpecSheetsUploadPlan t_plan = this.sheetsPlan;
        if (this.sheetsBusy || t_plan == null || !t_plan.CanUpload || t_plan.ChangedCellCount <= 0) return;
        var t_names = this.sheetsPreviewFiles.ConvertAll(Path.GetFileName);
        if (!EditorUtility.DisplayDialog("Google Sheet 업로드 확인",
                $"대상: {t_plan.SpreadsheetTitle}\n문서 ID: {t_plan.SpreadsheetId}\n" +
                $"CSV {t_names.Count}개 / 변경 셀 {t_plan.ChangedCellCount}개\n\n" + string.Join("\n", t_names) +
                "\n\n미리보기에서 확인한 셀 변경과 삭제를 반영한다.", "업로드", "취소")) return;

        this.sheetsPlan = null;
        this.sheetsError = null;
        CancellationTokenSource t_cancel = BeginSheetsOperation("업로드");
        try
        {
            if (!SpecGoogleSheetsAuth.TryGetAccessToken(out string t_token, out string t_error))
            {
                this.sheetsError = t_error;
                return;
            }
            string t_report = await SpecSheetsUploader.UploadAsync(t_plan, t_token, t_cancel.Token);
            t_cancel.Token.ThrowIfCancellationRequested();
            if (IsCurrentSheetsOperation(t_cancel)) this.sheetsReport = t_report;
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentSheetsOperation(t_cancel)) this.sheetsReport = "업로드를 취소했다. 요청이 이미 전송됐을 수 있으므로 미리보기로 원격 상태를 다시 확인한다.";
        }
        catch (InvalidOperationException t_exception)
        {
            if (IsCurrentSheetsOperation(t_cancel)) this.sheetsError = t_exception.Message;
        }
        catch (Exception)
        {
            if (IsCurrentSheetsOperation(t_cancel)) this.sheetsError = "업로드에 실패했다. 미리보기로 원격 상태를 다시 확인한 뒤 재시도한다.";
        }
        finally { EndSheetsOperation(t_cancel); }
    }

    CancellationTokenSource BeginSheetsOperation(string _operation)
    {
        this.sheetsBusy = true;
        this.sheetsOperation = _operation;
        this.sheetsCancellation = new CancellationTokenSource();
        return this.sheetsCancellation;
    }

    bool IsCurrentSheetsOperation(CancellationTokenSource _cancellation)
        => this != null && ReferenceEquals(this.sheetsCancellation, _cancellation);

    void EndSheetsOperation(CancellationTokenSource _cancellation)
    {
        if (ReferenceEquals(this.sheetsCancellation, _cancellation))
        {
            this.sheetsCancellation = null;
            this.sheetsBusy = false;
        }
        _cancellation.Dispose();
        if (this != null) Repaint();
    }

    void DrawSheetsReport()
    {
        if (string.IsNullOrEmpty(this.sheetsReport)) return;
        Header("미리보기 · 결과");
        this.sheetsReportStyle ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };
        this.sheetsReportScroll = EditorGUILayout.BeginScrollView(this.sheetsReportScroll,
            GUILayout.MinHeight(180), GUILayout.MaxHeight(420));
        float t_height = this.sheetsReportStyle.CalcHeight(new GUIContent(this.sheetsReport), Mathf.Max(240, this.position.width - 58));
        EditorGUILayout.SelectableLabel(this.sheetsReport, this.sheetsReportStyle, GUILayout.MinHeight(t_height));
        EditorGUILayout.EndScrollView();
    }
}
