using System;

namespace MPMS.Helpers
{
    public static class DbErrorTranslationHelper
    {
        public static string TranslateException(Exception ex, string defaultMessage)
        {
            if (ex == null) return defaultMessage;

            // Gather all messages from inner exceptions
            string fullMessage = ex.ToString();

            // Match constraints to friendly descriptions
            if (fullMessage.Contains("FK_MPMS_WEEKLY_SNAPSHOT_PARENT"))
            {
                return "此週會的快照正被其他週會的「修正版快照」所引用作為父快照。若要刪除此週會，需先刪除後續建立的修正快照。";
            }
            if (fullMessage.Contains("FK_MPMS_PROJECT_PHASE_PROJECT"))
            {
                return "無法刪除此專案，因為該專案底下已建立了專案階段。請先刪除該專案底下的專案階段。";
            }
            if (fullMessage.Contains("FK_MPMS_TASK_PHASE"))
            {
                return "無法刪除此專案階段，因為此階段底下仍有任務存在。請先刪除或移交該階段底下的所有任務。";
            }
            if (fullMessage.Contains("FK_MPMS_TD_TASK") || fullMessage.Contains("FK_MPMS_TD_PREDECESSOR"))
            {
                return "此任務與其他任務存在前後置依賴關係。請先在任務編輯中移除該任務的前後置依賴關係，再進行刪除。";
            }
            if (fullMessage.Contains("FK_MPMS_TASK_MILESTONE"))
            {
                return "無法刪除此里程碑，因為此里程碑底下仍有任務存在。請先移除或移交相關任務。";
            }
            if (fullMessage.Contains("FK_MPMS_TASK_OWNER"))
            {
                return "無法刪除此使用者，因為該使用者目前是某些任務的負責人。請先將這些任務重新指派給其他人員。";
            }
            if (fullMessage.Contains("FK_MPMS_TASK_CREATOR"))
            {
                return "無法刪除此使用者，因為該使用者曾建立過系統任務，系統需保留此歷史紀錄以供追溯。建議您保留帳號。";
            }
            if (fullMessage.Contains("FK_MPMS_TASK_UPDATER"))
            {
                return "無法刪除此使用者，因為該使用者曾更新過任務紀錄，系統需保留歷史軌跡以供備查。";
            }
            if (fullMessage.Contains("FK_MPMS_TASK_ASSIST_USER"))
            {
                return "無法刪除此使用者，因為該使用者目前是某些任務的協辦人。請先至相關任務中移除此協辦人員。";
            }
            if (fullMessage.Contains("FK_MPMS_TASK_ATTACHMENT_USER"))
            {
                return "無法刪除此使用者，因為該使用者曾上傳過任務附件，系統需保留此上傳歷史紀錄。";
            }
            if (fullMessage.Contains("FK_MPMS_TASK_REVIEW_USER"))
            {
                return "無法刪除此使用者，因為該使用者目前是某些任務的審查人。請先將這些任務的審查人變更為其他人員。";
            }
            if (fullMessage.Contains("FK_MPMS_TSL_USER"))
            {
                return "無法刪除此使用者，因為該使用者在系統中留有任務狀態異動軌跡。為確保稽核紀錄完整性，此帳號無法被刪除。";
            }
            if (fullMessage.Contains("FK_MPMS_MEETING_PROJECT"))
            {
                return "無法刪除此專案，因為此專案已存在相關聯的週會會議記錄。";
            }
            if (fullMessage.Contains("FK_MPMS_MEETING_HOST"))
            {
                return "無法刪除此使用者，因為該使用者曾主持過週會會議，系統需保留主持紀錄。";
            }
            if (fullMessage.Contains("FK_MPMS_ACTION_TASK"))
            {
                return "無法刪除此任務，因為此任務已關聯至週會的代辦事項 (Action Item)。請先在週會中處理或刪除該代辦事項。";
            }
            if (fullMessage.Contains("FK_MPMS_ACTION_OWNER"))
            {
                return "無法刪除此使用者，因為該使用者是某些週會代辦事項 (Action Item) 的負責人。請先重新分配該代辦事項的負責人。";
            }
            if (fullMessage.Contains("FK_MPMS_WEEKLY_SNAPSHOT_PROJECT"))
            {
                return "無法刪除此專案，因為此專案已存在封存的週報快照紀錄。";
            }
            if (fullMessage.Contains("FK_MPMS_WEEKLY_SNAPSHOT_MEETING"))
            {
                return "無法刪除此週會，因為此週會已封存產生週報快照。";
            }
            if (fullMessage.Contains("FK_MPMS_WEEKLY_SNAPSHOT_USER"))
            {
                return "無法刪除此使用者，因為該使用者曾進行過週報快照封存。";
            }
            if (fullMessage.Contains("FK_MPMS_NOTIFICATION_USER"))
            {
                return "無法刪除此使用者，因為該使用者在系統中留有通知接收紀錄。";
            }
            if (fullMessage.Contains("FK_MPMS_AUDIT_USER"))
            {
                return "無法刪除此使用者，因為該使用者在系統中留有操作稽核日誌 (Audit Log)。為確保系統安全合規，無法刪除此帳號，建議變更其角色或停用帳號。";
            }

            // General fallbacks for foreign key constraint violation
            if (fullMessage.Contains("REFERENCE 條件約束") || fullMessage.Contains("REFERENCE constraint") || fullMessage.Contains("foreign key") || fullMessage.Contains("FOREIGN KEY"))
            {
                return $"{defaultMessage}：此資料目前被其他功能所引用，基於資料完整性限制，暫時無法直接刪除。請確認已清理或轉移相關聯的資料。";
            }

            // Return default message if no specific SQL constraint is recognized
            return $"{defaultMessage}：{ex.Message}";
        }
    }
}
