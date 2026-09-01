--
-- PostgreSQL database dump
--

\restrict X0GVjgmpUNc5L8JmARqIn6ra5u1dwjshBLqcXIRUs1ksoWJaMiCdFV8He50hNaC

-- Dumped from database version 14.24 (Ubuntu 14.24-0ubuntu0.22.04.1)
-- Dumped by pg_dump version 14.24 (Ubuntu 14.24-0ubuntu0.22.04.1)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

ALTER TABLE IF EXISTS ONLY public.user_badges DROP CONSTRAINT IF EXISTS user_badges_badge_id_fkey;
ALTER TABLE IF EXISTS ONLY public.refunds DROP CONSTRAINT IF EXISTS refunds_order_id_fkey;
ALTER TABLE IF EXISTS ONLY public.features DROP CONSTRAINT IF EXISTS features_sprint_id_fkey;
ALTER TABLE IF EXISTS ONLY public."UserInterests" DROP CONSTRAINT IF EXISTS "UserInterests_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."UserInterests" DROP CONSTRAINT IF EXISTS "UserInterests_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."UserAIMemories" DROP CONSTRAINT IF EXISTS "UserAIMemories_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."TwoFactorTokens" DROP CONSTRAINT IF EXISTS "TwoFactorTokens_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."TeacherStudentLinks" DROP CONSTRAINT IF EXISTS "TeacherStudentLinks_TeacherId_fkey";
ALTER TABLE IF EXISTS ONLY public."TeacherStudentLinks" DROP CONSTRAINT IF EXISTS "TeacherStudentLinks_StudentId_fkey";
ALTER TABLE IF EXISTS ONLY public."TeacherStudentLinks" DROP CONSTRAINT IF EXISTS "TeacherStudentLinks_InitiatedBy_fkey";
ALTER TABLE IF EXISTS ONLY public."TeacherClasses" DROP CONSTRAINT IF EXISTS "TeacherClasses_TeacherId_fkey";
ALTER TABLE IF EXISTS ONLY public."TeacherClassStudents" DROP CONSTRAINT IF EXISTS "TeacherClassStudents_TeacherClassId_fkey";
ALTER TABLE IF EXISTS ONLY public."TeacherClassStudents" DROP CONSTRAINT IF EXISTS "TeacherClassStudents_StudentId_fkey";
ALTER TABLE IF EXISTS ONLY public."Subscriptions" DROP CONSTRAINT IF EXISTS "Subscriptions_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Subscriptions" DROP CONSTRAINT IF EXISTS "Subscriptions_PricingPlanId_fkey";
ALTER TABLE IF EXISTS ONLY public."StudyGroups" DROP CONSTRAINT IF EXISTS "StudyGroups_OwnerId_fkey";
ALTER TABLE IF EXISTS ONLY public."StudyGroupMembers" DROP CONSTRAINT IF EXISTS "StudyGroupMembers_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."StudyGroupMembers" DROP CONSTRAINT IF EXISTS "StudyGroupMembers_StudyGroupId_fkey";
ALTER TABLE IF EXISTS ONLY public."Revisions" DROP CONSTRAINT IF EXISTS "Revisions_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."Revisions" DROP CONSTRAINT IF EXISTS "Revisions_CreatedBy_fkey";
ALTER TABLE IF EXISTS ONLY public."RevisionTags" DROP CONSTRAINT IF EXISTS "RevisionTags_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."RevisionTags" DROP CONSTRAINT IF EXISTS "RevisionTags_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."RevisionNotes" DROP CONSTRAINT IF EXISTS "RevisionNotes_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."RevisionNotes" DROP CONSTRAINT IF EXISTS "RevisionNotes_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."RevisionEnrollments" DROP CONSTRAINT IF EXISTS "RevisionEnrollments_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."RevisionEnrollments" DROP CONSTRAINT IF EXISTS "RevisionEnrollments_RevisionId_fkey";
ALTER TABLE IF EXISTS ONLY public."Reviews" DROP CONSTRAINT IF EXISTS "Reviews_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Reviews" DROP CONSTRAINT IF EXISTS "Reviews_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."RefreshTokens" DROP CONSTRAINT IF EXISTS "RefreshTokens_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Quizzes" DROP CONSTRAINT IF EXISTS "Quizzes_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."Quizzes" DROP CONSTRAINT IF EXISTS "Quizzes_CreatedBy_fkey";
ALTER TABLE IF EXISTS ONLY public."QuizMistakes" DROP CONSTRAINT IF EXISTS "QuizMistakes_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."QuizMistakes" DROP CONSTRAINT IF EXISTS "QuizMistakes_QuizId_fkey";
ALTER TABLE IF EXISTS ONLY public."QuizAttempts" DROP CONSTRAINT IF EXISTS "QuizAttempts_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."QuizAttempts" DROP CONSTRAINT IF EXISTS "QuizAttempts_QuizId_fkey";
ALTER TABLE IF EXISTS ONLY public."PromoCodes" DROP CONSTRAINT IF EXISTS "PromoCodes_CreatedBy_fkey";
ALTER TABLE IF EXISTS ONLY public."PromoCodeUsages" DROP CONSTRAINT IF EXISTS "PromoCodeUsages_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."PromoCodeUsages" DROP CONSTRAINT IF EXISTS "PromoCodeUsages_PromoCodeId_fkey";
ALTER TABLE IF EXISTS ONLY public."PromoCodeUsages" DROP CONSTRAINT IF EXISTS "PromoCodeUsages_OrderId_fkey";
ALTER TABLE IF EXISTS ONLY public."PasswordResetTokens" DROP CONSTRAINT IF EXISTS "PasswordResetTokens_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."ParentStudentLinks" DROP CONSTRAINT IF EXISTS "ParentStudentLinks_StudentId_fkey";
ALTER TABLE IF EXISTS ONLY public."ParentStudentLinks" DROP CONSTRAINT IF EXISTS "ParentStudentLinks_ParentId_fkey";
ALTER TABLE IF EXISTS ONLY public."ParentCreditLedgers" DROP CONSTRAINT IF EXISTS "ParentCreditLedgers_ParentId_fkey";
ALTER TABLE IF EXISTS ONLY public."ParentCreditLedgers" DROP CONSTRAINT IF EXISTS "ParentCreditLedgers_ChildId_fkey";
ALTER TABLE IF EXISTS ONLY public."Pages" DROP CONSTRAINT IF EXISTS "Pages_UpdatedBy_fkey";
ALTER TABLE IF EXISTS ONLY public."Pages" DROP CONSTRAINT IF EXISTS "Pages_CreatedBy_fkey";
ALTER TABLE IF EXISTS ONLY public."OAuthAccounts" DROP CONSTRAINT IF EXISTS "OAuthAccounts_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Messages" DROP CONSTRAINT IF EXISTS "Messages_ConversationId_fkey";
ALTER TABLE IF EXISTS ONLY public."InstitutionStudents" DROP CONSTRAINT IF EXISTS "InstitutionStudents_StudentId_fkey";
ALTER TABLE IF EXISTS ONLY public."InstitutionStudents" DROP CONSTRAINT IF EXISTS "InstitutionStudents_InstitutionId_fkey";
ALTER TABLE IF EXISTS ONLY public."Goals" DROP CONSTRAINT IF EXISTS "Goals_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."ForumVotes" DROP CONSTRAINT IF EXISTS "ForumVotes_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."ForumVotes" DROP CONSTRAINT IF EXISTS "ForumVotes_PostId_fkey";
ALTER TABLE IF EXISTS ONLY public."ForumThreads" DROP CONSTRAINT IF EXISTS "ForumThreads_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."ForumPosts" DROP CONSTRAINT IF EXISTS "ForumPosts_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."ForumPosts" DROP CONSTRAINT IF EXISTS "ForumPosts_ThreadId_fkey";
ALTER TABLE IF EXISTS ONLY public."FocusSessions" DROP CONSTRAINT IF EXISTS "FocusSessions_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Payments" DROP CONSTRAINT IF EXISTS "FK_payments_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."Payments" DROP CONSTRAINT IF EXISTS "FK_payments_Orders_OrderId";
ALTER TABLE IF EXISTS ONLY public."WeeklyGoals" DROP CONSTRAINT IF EXISTS "FK_WeeklyGoals_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."Users" DROP CONSTRAINT IF EXISTS "FK_Users_Institutions_InstitutionId";
ALTER TABLE IF EXISTS ONLY public."UserTwoFactorAuthentication" DROP CONSTRAINT IF EXISTS "FK_UserTwoFactorAuthentication_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."UserSessions" DROP CONSTRAINT IF EXISTS "FK_UserSessions_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."UserPrivacySettings" DROP CONSTRAINT IF EXISTS "FK_UserPrivacySettings_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."UserNotificationSettings" DROP CONSTRAINT IF EXISTS "FK_UserNotificationSettings_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."Revisions" DROP CONSTRAINT IF EXISTS "FK_Revisions_Users_CreatedByUserId";
ALTER TABLE IF EXISTS ONLY public."Revisions" DROP CONSTRAINT IF EXISTS "FK_Revisions_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."Revisions" DROP CONSTRAINT IF EXISTS "FK_Revisions_Exams_ExamId";
ALTER TABLE IF EXISTS ONLY public."RevisionEnrollments" DROP CONSTRAINT IF EXISTS "FK_RevisionEnrollments_LearningHistories";
ALTER TABLE IF EXISTS ONLY public."Quizzes" DROP CONSTRAINT IF EXISTS "FK_Quizzes_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."Quizzes" DROP CONSTRAINT IF EXISTS "FK_Quizzes_Exams_ExamId";
ALTER TABLE IF EXISTS ONLY public."Orders" DROP CONSTRAINT IF EXISTS "FK_Orders_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."OrderItems" DROP CONSTRAINT IF EXISTS "FK_OrderItems_Orders_OrderId";
ALTER TABLE IF EXISTS ONLY public."OrderItems" DROP CONSTRAINT IF EXISTS "FK_OrderItems_Courses_CourseId";
ALTER TABLE IF EXISTS ONLY public."Notifications" DROP CONSTRAINT IF EXISTS "FK_Notifications_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."LessonProgress" DROP CONSTRAINT IF EXISTS "FK_LessonProgress_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."LessonProgress" DROP CONSTRAINT IF EXISTS "FK_LessonProgress_Courses_CourseId";
ALTER TABLE IF EXISTS ONLY public."LessonProgress" DROP CONSTRAINT IF EXISTS "FK_LessonProgress_CourseLessons_LessonId";
ALTER TABLE IF EXISTS ONLY public."LearningHistories" DROP CONSTRAINT IF EXISTS "FK_LearningHistories_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."LearningHistories" DROP CONSTRAINT IF EXISTS "FK_LearningHistories_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."LearningHistories" DROP CONSTRAINT IF EXISTS "FK_LearningHistories_CourseContents_ContentId";
ALTER TABLE IF EXISTS ONLY public."ForumThreadFollows" DROP CONSTRAINT IF EXISTS "FK_ForumThreadFollows_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."ForumThreadFollows" DROP CONSTRAINT IF EXISTS "FK_ForumThreadFollows_ForumThreads_ThreadId";
ALTER TABLE IF EXISTS ONLY public."Favorites" DROP CONSTRAINT IF EXISTS "FK_Favorites_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."Favorites" DROP CONSTRAINT IF EXISTS "FK_Favorites_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."Favorites" DROP CONSTRAINT IF EXISTS "FK_Favorites_FavoriteCollections_CollectionId";
ALTER TABLE IF EXISTS ONLY public."FavoriteCollections" DROP CONSTRAINT IF EXISTS "FK_FavoriteCollections_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."Exams" DROP CONSTRAINT IF EXISTS "FK_Exams_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."Enrollments" DROP CONSTRAINT IF EXISTS "FK_Enrollments_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."Enrollments" DROP CONSTRAINT IF EXISTS "FK_Enrollments_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."DownloadHistories" DROP CONSTRAINT IF EXISTS "FK_DownloadHistories_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."DownloadHistories" DROP CONSTRAINT IF EXISTS "FK_DownloadHistories_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."Courses" DROP CONSTRAINT IF EXISTS "FK_Courses_Users_InstructorId";
ALTER TABLE IF EXISTS ONLY public."CourseSections" DROP CONSTRAINT IF EXISTS "FK_CourseSections_Courses_CourseId";
ALTER TABLE IF EXISTS ONLY public."CourseReviews" DROP CONSTRAINT IF EXISTS "FK_CourseReviews_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."CourseReviews" DROP CONSTRAINT IF EXISTS "FK_CourseReviews_Courses_CourseId";
ALTER TABLE IF EXISTS ONLY public."CourseLessons" DROP CONSTRAINT IF EXISTS "FK_CourseLessons_Courses_CourseId";
ALTER TABLE IF EXISTS ONLY public."CourseLessons" DROP CONSTRAINT IF EXISTS "FK_CourseLessons_CourseSections_SectionId";
ALTER TABLE IF EXISTS ONLY public."CourseEnrollments" DROP CONSTRAINT IF EXISTS "FK_CourseEnrollments_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."CourseEnrollments" DROP CONSTRAINT IF EXISTS "FK_CourseEnrollments_Courses_CourseId";
ALTER TABLE IF EXISTS ONLY public."CourseContents" DROP CONSTRAINT IF EXISTS "FK_CourseContents_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."CartItems" DROP CONSTRAINT IF EXISTS "FK_CartItems_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."CartItems" DROP CONSTRAINT IF EXISTS "FK_CartItems_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."AnalyticsEvents" DROP CONSTRAINT IF EXISTS "FK_AnalyticsEvents_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."Exams" DROP CONSTRAINT IF EXISTS "Exams_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."EmailVerificationTokens" DROP CONSTRAINT IF EXISTS "EmailVerificationTokens_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."DirectMessages" DROP CONSTRAINT IF EXISTS "DirectMessages_ToUserId_fkey";
ALTER TABLE IF EXISTS ONLY public."DirectMessages" DROP CONSTRAINT IF EXISTS "DirectMessages_FromUserId_fkey";
ALTER TABLE IF EXISTS ONLY public."DeviceInfos" DROP CONSTRAINT IF EXISTS "DeviceInfos_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Conversations" DROP CONSTRAINT IF EXISTS "Conversations_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."ChatbotContexts" DROP CONSTRAINT IF EXISTS "ChatbotContexts_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Certificates" DROP CONSTRAINT IF EXISTS "Certificates_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Certificates" DROP CONSTRAINT IF EXISTS "Certificates_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."Certificates" DROP CONSTRAINT IF EXISTS "Certificates_EnrollmentId_fkey";
ALTER TABLE IF EXISTS ONLY public."BackupCodes" DROP CONSTRAINT IF EXISTS "BackupCodes_TwoFactorTokenId_fkey";
DROP TRIGGER IF EXISTS update_usertwofactorauthentication_updated_at ON public."UserTwoFactorAuthentication";
DROP TRIGGER IF EXISTS update_userprivacysettings_updated_at ON public."UserPrivacySettings";
DROP TRIGGER IF EXISTS update_usernotificationsettings_updated_at ON public."UserNotificationSettings";
DROP TRIGGER IF EXISTS update_sections_updated_at ON public."CourseSections";
DROP TRIGGER IF EXISTS update_reviews_updated_at ON public."CourseReviews";
DROP TRIGGER IF EXISTS update_lessons_updated_at ON public."CourseLessons";
DROP TRIGGER IF EXISTS update_lesson_progress_updated_at ON public."LessonProgress";
DROP TRIGGER IF EXISTS update_enrollments_updated_at ON public."CourseEnrollments";
DROP TRIGGER IF EXISTS update_courses_updated_at ON public."Courses";
DROP TRIGGER IF EXISTS trg_lessons_count ON public."CourseLessons";
DROP TRIGGER IF EXISTS trg_enrolled_count ON public."CourseEnrollments";
DROP TRIGGER IF EXISTS trg_course_rating ON public."CourseReviews";
DROP INDEX IF EXISTS public."ix_ForumModerationQueue_PostId";
DROP INDEX IF EXISTS public.idx_userinterests_user;
DROP INDEX IF EXISTS public.idx_user_profiles_user_id;
DROP INDEX IF EXISTS public.idx_user_profiles_role;
DROP INDEX IF EXISTS public.idx_two_factor_tokens_user_id;
DROP INDEX IF EXISTS public.idx_subscriptions_status;
DROP INDEX IF EXISTS public.idx_sprints_dates;
DROP INDEX IF EXISTS public.idx_revisions_subject;
DROP INDEX IF EXISTS public.idx_revisions_creator;
DROP INDEX IF EXISTS public.idx_refresh_tokens_user_id;
DROP INDEX IF EXISTS public.idx_refresh_tokens_token;
DROP INDEX IF EXISTS public.idx_refresh_tokens_expires_at;
DROP INDEX IF EXISTS public.idx_quizzes_subject;
DROP INDEX IF EXISTS public.idx_quizzes_creator;
DROP INDEX IF EXISTS public.idx_promocodeusages_user;
DROP INDEX IF EXISTS public.idx_promocodeusages_promocode;
DROP INDEX IF EXISTS public.idx_promocodes_validuntil;
DROP INDEX IF EXISTS public.idx_promocodes_code;
DROP INDEX IF EXISTS public.idx_promocodes_active;
DROP INDEX IF EXISTS public.idx_payments_status;
DROP INDEX IF EXISTS public.idx_payments_notchpay_ref;
DROP INDEX IF EXISTS public.idx_password_reset_tokens_user_id;
DROP INDEX IF EXISTS public.idx_password_reset_tokens_token;
DROP INDEX IF EXISTS public.idx_password_reset_tokens_expires;
DROP INDEX IF EXISTS public.idx_pages_slug;
DROP INDEX IF EXISTS public.idx_orders_user;
DROP INDEX IF EXISTS public.idx_orders_status;
DROP INDEX IF EXISTS public.idx_orders_created;
DROP INDEX IF EXISTS public.idx_oauth_accounts_user_id;
DROP INDEX IF EXISTS public.idx_oauth_accounts_unique;
DROP INDEX IF EXISTS public.idx_notifications_user;
DROP INDEX IF EXISTS public.idx_notifications_read;
DROP INDEX IF EXISTS public.idx_messages_conversation;
DROP INDEX IF EXISTS public.idx_goals_user;
DROP INDEX IF EXISTS public.idx_forum_threads_user;
DROP INDEX IF EXISTS public.idx_forum_threads_category;
DROP INDEX IF EXISTS public.idx_forum_posts_user;
DROP INDEX IF EXISTS public.idx_forum_posts_thread;
DROP INDEX IF EXISTS public.idx_features_status;
DROP INDEX IF EXISTS public.idx_features_sprint;
DROP INDEX IF EXISTS public.idx_exams_year;
DROP INDEX IF EXISTS public.idx_exams_type;
DROP INDEX IF EXISTS public.idx_exams_subject;
DROP INDEX IF EXISTS public.idx_exams_category;
DROP INDEX IF EXISTS public.idx_enroll_not_deleted;
DROP INDEX IF EXISTS public.idx_email_verification_tokens_user_id;
DROP INDEX IF EXISTS public.idx_email_verification_tokens_expires;
DROP INDEX IF EXISTS public.idx_email_verification_tokens_code;
DROP INDEX IF EXISTS public.idx_dm_to;
DROP INDEX IF EXISTS public.idx_dm_from;
DROP INDEX IF EXISTS public.idx_device_infos_user_id;
DROP INDEX IF EXISTS public.idx_device_infos_unique;
DROP INDEX IF EXISTS public.idx_device_infos_remember;
DROP INDEX IF EXISTS public.idx_device_infos_fingerprint;
DROP INDEX IF EXISTS public.idx_coupons_code;
DROP INDEX IF EXISTS public.idx_conversations_user;
DROP INDEX IF EXISTS public.idx_certificates_user;
DROP INDEX IF EXISTS public.idx_certificates_enrollment;
DROP INDEX IF EXISTS public.idx_backup_codes_two_factor_id;
DROP INDEX IF EXISTS public.idx_backup_codes_code;
DROP INDEX IF EXISTS public.idx_analytics_events_user;
DROP INDEX IF EXISTS public.idx_analytics_events_type;
DROP INDEX IF EXISTS public.idx_analytics_events_created;
DROP INDEX IF EXISTS public.idx_aim_user;
DROP INDEX IF EXISTS public.idx_abuse_reports_status;
DROP INDEX IF EXISTS public.idx_abuse_reports_created;
DROP INDEX IF EXISTS public."UQ_LessonProgress_UserId_LessonId";
DROP INDEX IF EXISTS public."UQ_CourseSections_CourseId_Position";
DROP INDEX IF EXISTS public."UQ_CourseReviews_UserId_CourseId";
DROP INDEX IF EXISTS public."UQ_CourseLessons_SectionId_Position";
DROP INDEX IF EXISTS public."UQ_CourseEnrollments_UserId_CourseId";
DROP INDEX IF EXISTS public."IX_payments_UserId";
DROP INDEX IF EXISTS public."IX_payments_TransactionId";
DROP INDEX IF EXISTS public."IX_payments_OrderId";
DROP INDEX IF EXISTS public."IX_WeeklyGoals_UserId_WeekStart";
DROP INDEX IF EXISTS public."IX_WeeklyGoals_UserId";
DROP INDEX IF EXISTS public."IX_Users_InstitutionId";
DROP INDEX IF EXISTS public."IX_Users_Email";
DROP INDEX IF EXISTS public."IX_Users_CognitoId";
DROP INDEX IF EXISTS public."IX_UserTwoFactorAuthentication_UserId";
DROP INDEX IF EXISTS public."IX_UserSessions_UserId";
DROP INDEX IF EXISTS public."IX_UserSessions_IsActive";
DROP INDEX IF EXISTS public."IX_UserSessions_ExpiresAt";
DROP INDEX IF EXISTS public."IX_UserPrivacySettings_UserId";
DROP INDEX IF EXISTS public."IX_UserNotificationSettings_UserId";
DROP INDEX IF EXISTS public."IX_TeacherClasses_TeacherId";
DROP INDEX IF EXISTS public."IX_TeacherClassStudents_Class_Student";
DROP INDEX IF EXISTS public."IX_StudyGroups_OwnerId";
DROP INDEX IF EXISTS public."IX_StudyGroups_JoinCode";
DROP INDEX IF EXISTS public."IX_StudyGroupMembers_Group_User";
DROP INDEX IF EXISTS public."IX_Revisions_SubjectId";
DROP INDEX IF EXISTS public."IX_Revisions_IsDeleted";
DROP INDEX IF EXISTS public."IX_Revisions_ExamId";
DROP INDEX IF EXISTS public."IX_Revisions_CreatedAt";
DROP INDEX IF EXISTS public."IX_RevisionTags_User_Subject_Label";
DROP INDEX IF EXISTS public."IX_RevisionNotes_User_Subject";
DROP INDEX IF EXISTS public."IX_RevisionEnrollments_User_Status";
DROP INDEX IF EXISTS public."IX_RevisionEnrollments_UserId";
DROP INDEX IF EXISTS public."IX_RevisionEnrollments_RevisionId";
DROP INDEX IF EXISTS public."IX_Quizzes_SubjectId";
DROP INDEX IF EXISTS public."IX_Quizzes_IsPublished";
DROP INDEX IF EXISTS public."IX_Quizzes_ExamId";
DROP INDEX IF EXISTS public."IX_QuizMistakes_User_Resolved";
DROP INDEX IF EXISTS public."IX_QuizMistakes_UserId_IsResolved";
DROP INDEX IF EXISTS public."IX_QuizMistakes_CreatedAt";
DROP INDEX IF EXISTS public."IX_QuizAttempts_User_Completed";
DROP INDEX IF EXISTS public."IX_QuizAttempts_UserId";
DROP INDEX IF EXISTS public."IX_QuizAttempts_QuizId";
DROP INDEX IF EXISTS public."IX_QuizAttempts_CompletedAt";
DROP INDEX IF EXISTS public."IX_ParentCreditLedgers_Parent_Period";
DROP INDEX IF EXISTS public."IX_Orders_UserId";
DROP INDEX IF EXISTS public."IX_Orders_OrderNumber";
DROP INDEX IF EXISTS public."IX_OrderItems_OrderId";
DROP INDEX IF EXISTS public."IX_Notifications_UserId";
DROP INDEX IF EXISTS public."IX_Messages_IsDeleted";
DROP INDEX IF EXISTS public."IX_Messages_CreatedAt";
DROP INDEX IF EXISTS public."IX_Messages_ConversationId";
DROP INDEX IF EXISTS public."IX_LessonProgress_UserId";
DROP INDEX IF EXISTS public."IX_LessonProgress_CourseId";
DROP INDEX IF EXISTS public."IX_LearningHistories_UserId";
DROP INDEX IF EXISTS public."IX_LearningHistories_SubjectId";
DROP INDEX IF EXISTS public."IX_LearningHistories_ContentId";
DROP INDEX IF EXISTS public."IX_InstitutionStudents_Institution_Student";
DROP INDEX IF EXISTS public."IX_InstitutionStudents_InstitutionId";
DROP INDEX IF EXISTS public."IX_ForumVotes_PostId";
DROP INDEX IF EXISTS public."IX_ForumThreads_UserId";
DROP INDEX IF EXISTS public."IX_ForumThreads_RepliesCount";
DROP INDEX IF EXISTS public."IX_ForumThreads_CreatedAt";
DROP INDEX IF EXISTS public."IX_ForumThreads_Category_CreatedAt";
DROP INDEX IF EXISTS public."IX_ForumThreads_Category";
DROP INDEX IF EXISTS public."IX_ForumThreadFollows_UserId_ThreadId";
DROP INDEX IF EXISTS public."IX_ForumThreadFollows_ThreadId";
DROP INDEX IF EXISTS public."IX_ForumPosts_UserId";
DROP INDEX IF EXISTS public."IX_ForumPosts_ThreadId";
DROP INDEX IF EXISTS public."IX_FocusSessions_UserId_StartedAt";
DROP INDEX IF EXISTS public."IX_Favorites_UserId_SubjectId";
DROP INDEX IF EXISTS public."IX_Favorites_SubjectId";
DROP INDEX IF EXISTS public."IX_Favorites_CollectionId";
DROP INDEX IF EXISTS public."IX_FavoriteCollections_UserId";
DROP INDEX IF EXISTS public."IX_FavoriteCollections_Name_UserId";
DROP INDEX IF EXISTS public."IX_Exams_SubjectId";
DROP INDEX IF EXISTS public."IX_Exams_ExamType";
DROP INDEX IF EXISTS public."IX_Enrollments_UserId_SubjectId";
DROP INDEX IF EXISTS public."IX_Enrollments_SubjectId";
DROP INDEX IF EXISTS public."IX_DownloadHistories_UserId_CreatedAt";
DROP INDEX IF EXISTS public."IX_DownloadHistories_SubjectId";
DROP INDEX IF EXISTS public."IX_Courses_Status";
DROP INDEX IF EXISTS public."IX_Courses_InstructorId";
DROP INDEX IF EXISTS public."IX_Courses_Category";
DROP INDEX IF EXISTS public."IX_CourseSections_CourseId";
DROP INDEX IF EXISTS public."IX_CourseReviews_CourseId";
DROP INDEX IF EXISTS public."IX_CourseLessons_SectionId";
DROP INDEX IF EXISTS public."IX_CourseLessons_CourseId";
DROP INDEX IF EXISTS public."IX_CourseEnrollments_UserId";
DROP INDEX IF EXISTS public."IX_CourseEnrollments_CourseId";
DROP INDEX IF EXISTS public."IX_CourseContents_SubjectId";
DROP INDEX IF EXISTS public."IX_CourseContents_Status";
DROP INDEX IF EXISTS public."IX_CourseContents_CreatedByUserId";
DROP INDEX IF EXISTS public."IX_Conversations_UserId";
DROP INDEX IF EXISTS public."IX_Conversations_LastMessageAt";
DROP INDEX IF EXISTS public."IX_Conversations_IsDeleted";
DROP INDEX IF EXISTS public."IX_Conversations_IsActive";
DROP INDEX IF EXISTS public."IX_ConcoursEvents_Slug_Year";
DROP INDEX IF EXISTS public."IX_ChatbotContexts_UserId";
DROP INDEX IF EXISTS public."IX_CartItems_UserId_SubjectId";
DROP INDEX IF EXISTS public."IX_CartItems_SubjectId";
DROP INDEX IF EXISTS public."IX_ApplicationLogs_Level";
DROP INDEX IF EXISTS public."IX_ApplicationLogs_IsResolved";
DROP INDEX IF EXISTS public."IX_ApplicationLogs_CreatedAt";
DROP INDEX IF EXISTS public."IX_AnalyticsEvents_UserId";
ALTER TABLE IF EXISTS ONLY public.user_profiles DROP CONSTRAINT IF EXISTS user_profiles_user_id_key;
ALTER TABLE IF EXISTS ONLY public.user_profiles DROP CONSTRAINT IF EXISTS user_profiles_pkey;
ALTER TABLE IF EXISTS ONLY public.user_preferences DROP CONSTRAINT IF EXISTS user_preferences_user_id_key;
ALTER TABLE IF EXISTS ONLY public.user_preferences DROP CONSTRAINT IF EXISTS user_preferences_pkey;
ALTER TABLE IF EXISTS ONLY public.user_badges DROP CONSTRAINT IF EXISTS user_badges_user_id_badge_id_key;
ALTER TABLE IF EXISTS ONLY public.user_badges DROP CONSTRAINT IF EXISTS user_badges_pkey;
ALTER TABLE IF EXISTS ONLY public."ForumVotes" DROP CONSTRAINT IF EXISTS uq_forum_votes_post_user;
ALTER TABLE IF EXISTS ONLY public.sprints DROP CONSTRAINT IF EXISTS sprints_pkey;
ALTER TABLE IF EXISTS ONLY public.refunds DROP CONSTRAINT IF EXISTS refunds_pkey;
ALTER TABLE IF EXISTS ONLY public.orders DROP CONSTRAINT IF EXISTS orders_pkey;
ALTER TABLE IF EXISTS ONLY public.orders DROP CONSTRAINT IF EXISTS orders_order_number_key;
ALTER TABLE IF EXISTS ONLY public.notifications DROP CONSTRAINT IF EXISTS notifications_pkey;
ALTER TABLE IF EXISTS ONLY public.features DROP CONSTRAINT IF EXISTS features_pkey;
ALTER TABLE IF EXISTS ONLY public.daily_statistics DROP CONSTRAINT IF EXISTS daily_statistics_stat_date_key;
ALTER TABLE IF EXISTS ONLY public.daily_statistics DROP CONSTRAINT IF EXISTS daily_statistics_pkey;
ALTER TABLE IF EXISTS ONLY public.coupons DROP CONSTRAINT IF EXISTS coupons_pkey;
ALTER TABLE IF EXISTS ONLY public.coupons DROP CONSTRAINT IF EXISTS coupons_code_key;
ALTER TABLE IF EXISTS ONLY public.cohort_analytics DROP CONSTRAINT IF EXISTS cohort_analytics_pkey;
ALTER TABLE IF EXISTS ONLY public.badges DROP CONSTRAINT IF EXISTS badges_pkey;
ALTER TABLE IF EXISTS ONLY public.badges DROP CONSTRAINT IF EXISTS badges_name_key;
ALTER TABLE IF EXISTS ONLY public.analytics_events DROP CONSTRAINT IF EXISTS analytics_events_pkey;
ALTER TABLE IF EXISTS ONLY public.abuse_reports DROP CONSTRAINT IF EXISTS abuse_reports_pkey;
ALTER TABLE IF EXISTS ONLY public."WeeklyGoals" DROP CONSTRAINT IF EXISTS "WeeklyGoals_pkey";
ALTER TABLE IF EXISTS ONLY public."WebhookIdempotencyKeys" DROP CONSTRAINT IF EXISTS "WebhookIdempotencyKeys_pkey";
ALTER TABLE IF EXISTS ONLY public."WebhookIdempotencyKeys" DROP CONSTRAINT IF EXISTS "WebhookIdempotencyKeys_Reference_key";
ALTER TABLE IF EXISTS ONLY public."UserTwoFactorAuthentication" DROP CONSTRAINT IF EXISTS "UserTwoFactorAuthentication_pkey";
ALTER TABLE IF EXISTS ONLY public."UserSessions" DROP CONSTRAINT IF EXISTS "UserSessions_pkey";
ALTER TABLE IF EXISTS ONLY public."UserPrivacySettings" DROP CONSTRAINT IF EXISTS "UserPrivacySettings_pkey";
ALTER TABLE IF EXISTS ONLY public."UserNotificationSettings" DROP CONSTRAINT IF EXISTS "UserNotificationSettings_pkey";
ALTER TABLE IF EXISTS ONLY public."UserInterests" DROP CONSTRAINT IF EXISTS "UserInterests_pkey";
ALTER TABLE IF EXISTS ONLY public."UserInterests" DROP CONSTRAINT IF EXISTS "UserInterests_UserId_SubjectId_key";
ALTER TABLE IF EXISTS ONLY public."UserAIMemories" DROP CONSTRAINT IF EXISTS "UserAIMemories_pkey";
ALTER TABLE IF EXISTS ONLY public."UserTwoFactorAuthentication" DROP CONSTRAINT IF EXISTS "UQ_UserTwoFactorAuthentication_UserId";
ALTER TABLE IF EXISTS ONLY public."UserPrivacySettings" DROP CONSTRAINT IF EXISTS "UQ_UserPrivacySettings_UserId";
ALTER TABLE IF EXISTS ONLY public."UserNotificationSettings" DROP CONSTRAINT IF EXISTS "UQ_UserNotificationSettings_UserId";
ALTER TABLE IF EXISTS ONLY public."Courses" DROP CONSTRAINT IF EXISTS "UQ_Courses_Slug";
ALTER TABLE IF EXISTS ONLY public."TwoFactorTokens" DROP CONSTRAINT IF EXISTS "TwoFactorTokens_pkey";
ALTER TABLE IF EXISTS ONLY public."TwoFactorTokens" DROP CONSTRAINT IF EXISTS "TwoFactorTokens_UserId_key";
ALTER TABLE IF EXISTS ONLY public."TeacherStudentLinks" DROP CONSTRAINT IF EXISTS "TeacherStudentLinks_pkey";
ALTER TABLE IF EXISTS ONLY public."TeacherStudentLinks" DROP CONSTRAINT IF EXISTS "TeacherStudentLinks_TeacherId_StudentId_key";
ALTER TABLE IF EXISTS ONLY public."TeacherClasses" DROP CONSTRAINT IF EXISTS "TeacherClasses_pkey";
ALTER TABLE IF EXISTS ONLY public."TeacherClassStudents" DROP CONSTRAINT IF EXISTS "TeacherClassStudents_pkey";
ALTER TABLE IF EXISTS ONLY public."Subscriptions" DROP CONSTRAINT IF EXISTS "Subscriptions_pkey";
ALTER TABLE IF EXISTS ONLY public."StudyGroups" DROP CONSTRAINT IF EXISTS "StudyGroups_pkey";
ALTER TABLE IF EXISTS ONLY public."StudyGroupMembers" DROP CONSTRAINT IF EXISTS "StudyGroupMembers_pkey";
ALTER TABLE IF EXISTS ONLY public."Sessions" DROP CONSTRAINT IF EXISTS "Sessions_pkey";
ALTER TABLE IF EXISTS ONLY public."Revisions" DROP CONSTRAINT IF EXISTS "Revisions_pkey";
ALTER TABLE IF EXISTS ONLY public."RevisionTags" DROP CONSTRAINT IF EXISTS "RevisionTags_pkey";
ALTER TABLE IF EXISTS ONLY public."RevisionNotes" DROP CONSTRAINT IF EXISTS "RevisionNotes_pkey";
ALTER TABLE IF EXISTS ONLY public."RevisionEnrollments" DROP CONSTRAINT IF EXISTS "RevisionEnrollments_pkey";
ALTER TABLE IF EXISTS ONLY public."Reviews" DROP CONSTRAINT IF EXISTS "Reviews_pkey";
ALTER TABLE IF EXISTS ONLY public."RefreshTokens" DROP CONSTRAINT IF EXISTS "RefreshTokens_pkey";
ALTER TABLE IF EXISTS ONLY public."RefreshTokens" DROP CONSTRAINT IF EXISTS "RefreshTokens_Token_key";
ALTER TABLE IF EXISTS ONLY public."Quizzes" DROP CONSTRAINT IF EXISTS "Quizzes_pkey";
ALTER TABLE IF EXISTS ONLY public."QuizMistakes" DROP CONSTRAINT IF EXISTS "QuizMistakes_pkey";
ALTER TABLE IF EXISTS ONLY public."QuizAttempts" DROP CONSTRAINT IF EXISTS "QuizAttempts_pkey";
ALTER TABLE IF EXISTS ONLY public."Promotions" DROP CONSTRAINT IF EXISTS "Promotions_pkey";
ALTER TABLE IF EXISTS ONLY public."Promotions" DROP CONSTRAINT IF EXISTS "Promotions_Code_key";
ALTER TABLE IF EXISTS ONLY public."PromoCodes" DROP CONSTRAINT IF EXISTS "PromoCodes_pkey";
ALTER TABLE IF EXISTS ONLY public."PromoCodes" DROP CONSTRAINT IF EXISTS "PromoCodes_Code_key";
ALTER TABLE IF EXISTS ONLY public."PromoCodeUsages" DROP CONSTRAINT IF EXISTS "PromoCodeUsages_pkey";
ALTER TABLE IF EXISTS ONLY public."PricingPlans" DROP CONSTRAINT IF EXISTS "PricingPlans_pkey";
ALTER TABLE IF EXISTS ONLY public."PlatformAnomalies" DROP CONSTRAINT IF EXISTS "PlatformAnomalies_pkey";
ALTER TABLE IF EXISTS ONLY public."PasswordResetTokens" DROP CONSTRAINT IF EXISTS "PasswordResetTokens_pkey";
ALTER TABLE IF EXISTS ONLY public."PasswordResetTokens" DROP CONSTRAINT IF EXISTS "PasswordResetTokens_Token_key";
ALTER TABLE IF EXISTS ONLY public."ParentStudentLinks" DROP CONSTRAINT IF EXISTS "ParentStudentLinks_pkey";
ALTER TABLE IF EXISTS ONLY public."ParentStudentLinks" DROP CONSTRAINT IF EXISTS "ParentStudentLinks_ParentId_StudentId_key";
ALTER TABLE IF EXISTS ONLY public."ParentCreditLedgers" DROP CONSTRAINT IF EXISTS "ParentCreditLedgers_pkey";
ALTER TABLE IF EXISTS ONLY public."Pages" DROP CONSTRAINT IF EXISTS "Pages_pkey";
ALTER TABLE IF EXISTS ONLY public."Pages" DROP CONSTRAINT IF EXISTS "Pages_Slug_key";
ALTER TABLE IF EXISTS ONLY public."Payments" DROP CONSTRAINT IF EXISTS "PK_payments";
ALTER TABLE IF EXISTS ONLY public."__EFMigrationsHistory" DROP CONSTRAINT IF EXISTS "PK___EFMigrationsHistory";
ALTER TABLE IF EXISTS ONLY public."Users" DROP CONSTRAINT IF EXISTS "PK_Users";
ALTER TABLE IF EXISTS ONLY public."Subjects" DROP CONSTRAINT IF EXISTS "PK_Subjects";
ALTER TABLE IF EXISTS ONLY public."Orders" DROP CONSTRAINT IF EXISTS "PK_Orders";
ALTER TABLE IF EXISTS ONLY public."OrderItems" DROP CONSTRAINT IF EXISTS "PK_OrderItems";
ALTER TABLE IF EXISTS ONLY public."Notifications" DROP CONSTRAINT IF EXISTS "PK_Notifications";
ALTER TABLE IF EXISTS ONLY public."LearningHistories" DROP CONSTRAINT IF EXISTS "PK_LearningHistories";
ALTER TABLE IF EXISTS ONLY public."Favorites" DROP CONSTRAINT IF EXISTS "PK_Favorites";
ALTER TABLE IF EXISTS ONLY public."Enrollments" DROP CONSTRAINT IF EXISTS "PK_Enrollments";
ALTER TABLE IF EXISTS ONLY public."CourseContents" DROP CONSTRAINT IF EXISTS "PK_CourseContents";
ALTER TABLE IF EXISTS ONLY public."CartItems" DROP CONSTRAINT IF EXISTS "PK_CartItems";
ALTER TABLE IF EXISTS ONLY public."AnalyticsEvents" DROP CONSTRAINT IF EXISTS "PK_AnalyticsEvents";
ALTER TABLE IF EXISTS ONLY public."OAuthAccounts" DROP CONSTRAINT IF EXISTS "OAuthAccounts_pkey";
ALTER TABLE IF EXISTS ONLY public."Messages" DROP CONSTRAINT IF EXISTS "Messages_pkey";
ALTER TABLE IF EXISTS ONLY public."Levels" DROP CONSTRAINT IF EXISTS "Levels_pkey";
ALTER TABLE IF EXISTS ONLY public."Levels" DROP CONSTRAINT IF EXISTS "Levels_Name_key";
ALTER TABLE IF EXISTS ONLY public."LessonProgress" DROP CONSTRAINT IF EXISTS "LessonProgress_pkey";
ALTER TABLE IF EXISTS ONLY public."Institutions" DROP CONSTRAINT IF EXISTS "Institutions_pkey";
ALTER TABLE IF EXISTS ONLY public."InstitutionStudents" DROP CONSTRAINT IF EXISTS "InstitutionStudents_pkey";
ALTER TABLE IF EXISTS ONLY public."HomePageFeatures" DROP CONSTRAINT IF EXISTS "HomePageFeatures_pkey";
ALTER TABLE IF EXISTS ONLY public."Goals" DROP CONSTRAINT IF EXISTS "Goals_pkey";
ALTER TABLE IF EXISTS ONLY public."ForumVotes" DROP CONSTRAINT IF EXISTS "ForumVotes_pkey";
ALTER TABLE IF EXISTS ONLY public."ForumThreads" DROP CONSTRAINT IF EXISTS "ForumThreads_pkey";
ALTER TABLE IF EXISTS ONLY public."ForumThreadFollows" DROP CONSTRAINT IF EXISTS "ForumThreadFollows_pkey";
ALTER TABLE IF EXISTS ONLY public."ForumPosts" DROP CONSTRAINT IF EXISTS "ForumPosts_pkey";
ALTER TABLE IF EXISTS ONLY public."ForumModerationQueue" DROP CONSTRAINT IF EXISTS "ForumModerationQueue_pkey";
ALTER TABLE IF EXISTS ONLY public."FocusSessions" DROP CONSTRAINT IF EXISTS "FocusSessions_pkey";
ALTER TABLE IF EXISTS ONLY public."FavoriteCollections" DROP CONSTRAINT IF EXISTS "FavoriteCollections_pkey";
ALTER TABLE IF EXISTS ONLY public."Exams" DROP CONSTRAINT IF EXISTS "Exams_pkey";
ALTER TABLE IF EXISTS ONLY public."Events" DROP CONSTRAINT IF EXISTS "Events_pkey";
ALTER TABLE IF EXISTS ONLY public."EmailVerificationTokens" DROP CONSTRAINT IF EXISTS "EmailVerificationTokens_pkey";
ALTER TABLE IF EXISTS ONLY public."DownloadHistories" DROP CONSTRAINT IF EXISTS "DownloadHistories_pkey";
ALTER TABLE IF EXISTS ONLY public."DirectMessages" DROP CONSTRAINT IF EXISTS "DirectMessages_pkey";
ALTER TABLE IF EXISTS ONLY public."DeviceInfos" DROP CONSTRAINT IF EXISTS "DeviceInfos_pkey";
ALTER TABLE IF EXISTS ONLY public."Courses" DROP CONSTRAINT IF EXISTS "Courses_pkey";
ALTER TABLE IF EXISTS ONLY public."CourseSections" DROP CONSTRAINT IF EXISTS "CourseSections_pkey";
ALTER TABLE IF EXISTS ONLY public."CourseReviews" DROP CONSTRAINT IF EXISTS "CourseReviews_pkey";
ALTER TABLE IF EXISTS ONLY public."CourseLessons" DROP CONSTRAINT IF EXISTS "CourseLessons_pkey";
ALTER TABLE IF EXISTS ONLY public."CourseEnrollments" DROP CONSTRAINT IF EXISTS "CourseEnrollments_pkey";
ALTER TABLE IF EXISTS ONLY public."Conversations" DROP CONSTRAINT IF EXISTS "Conversations_pkey";
ALTER TABLE IF EXISTS ONLY public."ConcoursEvents" DROP CONSTRAINT IF EXISTS "ConcoursEvents_pkey";
ALTER TABLE IF EXISTS ONLY public."ChatbotContexts" DROP CONSTRAINT IF EXISTS "ChatbotContexts_pkey";
ALTER TABLE IF EXISTS ONLY public."Certificates" DROP CONSTRAINT IF EXISTS "Certificates_pkey";
ALTER TABLE IF EXISTS ONLY public."Certificates" DROP CONSTRAINT IF EXISTS "Certificates_CertificateNumber_key";
ALTER TABLE IF EXISTS ONLY public."BackupCodes" DROP CONSTRAINT IF EXISTS "BackupCodes_pkey";
ALTER TABLE IF EXISTS ONLY public."BackupCodes" DROP CONSTRAINT IF EXISTS "BackupCodes_Code_key";
ALTER TABLE IF EXISTS ONLY public."ApplicationLogs" DROP CONSTRAINT IF EXISTS "ApplicationLogs_pkey";
ALTER TABLE IF EXISTS ONLY public."Announcements" DROP CONSTRAINT IF EXISTS "Announcements_pkey";
ALTER TABLE IF EXISTS public.user_profiles ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.user_preferences ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.user_badges ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.sprints ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.refunds ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.orders ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.notifications ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.features ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.daily_statistics ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.coupons ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.cohort_analytics ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.badges ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.analytics_events ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public.abuse_reports ALTER COLUMN id DROP DEFAULT;
ALTER TABLE IF EXISTS public."WebhookIdempotencyKeys" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."UserTwoFactorAuthentication" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."UserSessions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."UserPrivacySettings" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."UserNotificationSettings" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."UserInterests" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."UserAIMemories" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."TwoFactorTokens" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."TeacherStudentLinks" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."TeacherClasses" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."TeacherClassStudents" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Subscriptions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."StudyGroups" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."StudyGroupMembers" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Sessions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Revisions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."RevisionTags" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."RevisionNotes" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."RevisionEnrollments" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Reviews" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."RefreshTokens" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Quizzes" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."QuizMistakes" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."QuizAttempts" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Promotions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."PromoCodes" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."PromoCodeUsages" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."PricingPlans" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."PlatformAnomalies" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."PasswordResetTokens" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."ParentStudentLinks" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."ParentCreditLedgers" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Pages" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."OAuthAccounts" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Messages" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Levels" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."LessonProgress" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Institutions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."InstitutionStudents" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."HomePageFeatures" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Goals" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."ForumVotes" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."ForumThreads" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."ForumPosts" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."ForumModerationQueue" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."FocusSessions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."FavoriteCollections" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Exams" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Events" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."EmailVerificationTokens" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."DirectMessages" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."DeviceInfos" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Courses" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."CourseSections" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."CourseReviews" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."CourseLessons" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."CourseEnrollments" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Conversations" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."ConcoursEvents" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."ChatbotContexts" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Certificates" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."BackupCodes" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."ApplicationLogs" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Announcements" ALTER COLUMN "Id" DROP DEFAULT;
DROP SEQUENCE IF EXISTS public.user_profiles_id_seq;
DROP TABLE IF EXISTS public.user_profiles;
DROP SEQUENCE IF EXISTS public.user_preferences_id_seq;
DROP TABLE IF EXISTS public.user_preferences;
DROP SEQUENCE IF EXISTS public.user_badges_id_seq;
DROP TABLE IF EXISTS public.user_badges;
DROP SEQUENCE IF EXISTS public.sprints_id_seq;
DROP TABLE IF EXISTS public.sprints;
DROP SEQUENCE IF EXISTS public.refunds_id_seq;
DROP TABLE IF EXISTS public.refunds;
DROP SEQUENCE IF EXISTS public.orders_id_seq;
DROP TABLE IF EXISTS public.orders;
DROP SEQUENCE IF EXISTS public.notifications_id_seq;
DROP TABLE IF EXISTS public.notifications;
DROP SEQUENCE IF EXISTS public.features_id_seq;
DROP TABLE IF EXISTS public.features;
DROP SEQUENCE IF EXISTS public.daily_statistics_id_seq;
DROP TABLE IF EXISTS public.daily_statistics;
DROP SEQUENCE IF EXISTS public.coupons_id_seq;
DROP TABLE IF EXISTS public.coupons;
DROP SEQUENCE IF EXISTS public.cohort_analytics_id_seq;
DROP TABLE IF EXISTS public.cohort_analytics;
DROP SEQUENCE IF EXISTS public.badges_id_seq;
DROP TABLE IF EXISTS public.badges;
DROP SEQUENCE IF EXISTS public.analytics_events_id_seq;
DROP TABLE IF EXISTS public.analytics_events;
DROP SEQUENCE IF EXISTS public.abuse_reports_id_seq;
DROP TABLE IF EXISTS public.abuse_reports;
DROP TABLE IF EXISTS public."__EFMigrationsHistory";
DROP TABLE IF EXISTS public."WeeklyGoals";
DROP SEQUENCE IF EXISTS public."WebhookIdempotencyKeys_Id_seq";
DROP TABLE IF EXISTS public."WebhookIdempotencyKeys";
DROP TABLE IF EXISTS public."Users";
DROP SEQUENCE IF EXISTS public."UserTwoFactorAuthentication_Id_seq";
DROP TABLE IF EXISTS public."UserTwoFactorAuthentication";
DROP SEQUENCE IF EXISTS public."UserSessions_Id_seq";
DROP TABLE IF EXISTS public."UserSessions";
DROP SEQUENCE IF EXISTS public."UserPrivacySettings_Id_seq";
DROP TABLE IF EXISTS public."UserPrivacySettings";
DROP SEQUENCE IF EXISTS public."UserNotificationSettings_Id_seq";
DROP TABLE IF EXISTS public."UserNotificationSettings";
DROP SEQUENCE IF EXISTS public."UserInterests_Id_seq";
DROP TABLE IF EXISTS public."UserInterests";
DROP SEQUENCE IF EXISTS public."UserAIMemories_Id_seq";
DROP TABLE IF EXISTS public."UserAIMemories";
DROP SEQUENCE IF EXISTS public."TwoFactorTokens_Id_seq";
DROP TABLE IF EXISTS public."TwoFactorTokens";
DROP SEQUENCE IF EXISTS public."TeacherStudentLinks_Id_seq";
DROP TABLE IF EXISTS public."TeacherStudentLinks";
DROP SEQUENCE IF EXISTS public."TeacherClasses_Id_seq";
DROP TABLE IF EXISTS public."TeacherClasses";
DROP SEQUENCE IF EXISTS public."TeacherClassStudents_Id_seq";
DROP TABLE IF EXISTS public."TeacherClassStudents";
DROP SEQUENCE IF EXISTS public."Subscriptions_Id_seq";
DROP TABLE IF EXISTS public."Subscriptions";
DROP TABLE IF EXISTS public."Subjects";
DROP SEQUENCE IF EXISTS public."StudyGroups_Id_seq";
DROP TABLE IF EXISTS public."StudyGroups";
DROP SEQUENCE IF EXISTS public."StudyGroupMembers_Id_seq";
DROP TABLE IF EXISTS public."StudyGroupMembers";
DROP SEQUENCE IF EXISTS public."Sessions_Id_seq";
DROP TABLE IF EXISTS public."Sessions";
DROP SEQUENCE IF EXISTS public."Revisions_Id_seq";
DROP TABLE IF EXISTS public."Revisions";
DROP SEQUENCE IF EXISTS public."RevisionTags_Id_seq";
DROP TABLE IF EXISTS public."RevisionTags";
DROP SEQUENCE IF EXISTS public."RevisionNotes_Id_seq";
DROP TABLE IF EXISTS public."RevisionNotes";
DROP SEQUENCE IF EXISTS public."RevisionEnrollments_Id_seq";
DROP TABLE IF EXISTS public."RevisionEnrollments";
DROP SEQUENCE IF EXISTS public."Reviews_Id_seq";
DROP TABLE IF EXISTS public."Reviews";
DROP SEQUENCE IF EXISTS public."RefreshTokens_Id_seq";
DROP TABLE IF EXISTS public."RefreshTokens";
DROP SEQUENCE IF EXISTS public."Quizzes_Id_seq";
DROP TABLE IF EXISTS public."Quizzes";
DROP SEQUENCE IF EXISTS public."QuizMistakes_Id_seq";
DROP TABLE IF EXISTS public."QuizMistakes";
DROP SEQUENCE IF EXISTS public."QuizAttempts_Id_seq";
DROP TABLE IF EXISTS public."QuizAttempts";
DROP SEQUENCE IF EXISTS public."Promotions_Id_seq";
DROP TABLE IF EXISTS public."Promotions";
DROP SEQUENCE IF EXISTS public."PromoCodes_Id_seq";
DROP TABLE IF EXISTS public."PromoCodes";
DROP SEQUENCE IF EXISTS public."PromoCodeUsages_Id_seq";
DROP TABLE IF EXISTS public."PromoCodeUsages";
DROP SEQUENCE IF EXISTS public."PricingPlans_Id_seq";
DROP TABLE IF EXISTS public."PricingPlans";
DROP SEQUENCE IF EXISTS public."PlatformAnomalies_Id_seq";
DROP TABLE IF EXISTS public."PlatformAnomalies";
DROP TABLE IF EXISTS public."Payments";
DROP SEQUENCE IF EXISTS public."PasswordResetTokens_Id_seq";
DROP TABLE IF EXISTS public."PasswordResetTokens";
DROP SEQUENCE IF EXISTS public."ParentStudentLinks_Id_seq";
DROP TABLE IF EXISTS public."ParentStudentLinks";
DROP SEQUENCE IF EXISTS public."ParentCreditLedgers_Id_seq";
DROP TABLE IF EXISTS public."ParentCreditLedgers";
DROP SEQUENCE IF EXISTS public."Pages_Id_seq";
DROP TABLE IF EXISTS public."Pages";
DROP TABLE IF EXISTS public."Orders";
DROP TABLE IF EXISTS public."OrderItems";
DROP SEQUENCE IF EXISTS public."OAuthAccounts_Id_seq";
DROP TABLE IF EXISTS public."OAuthAccounts";
DROP TABLE IF EXISTS public."Notifications";
DROP SEQUENCE IF EXISTS public."Messages_Id_seq";
DROP TABLE IF EXISTS public."Messages";
DROP SEQUENCE IF EXISTS public."Levels_Id_seq";
DROP TABLE IF EXISTS public."Levels";
DROP SEQUENCE IF EXISTS public."LessonProgress_Id_seq";
DROP TABLE IF EXISTS public."LessonProgress";
DROP TABLE IF EXISTS public."LearningHistories";
DROP SEQUENCE IF EXISTS public."Institutions_Id_seq";
DROP TABLE IF EXISTS public."Institutions";
DROP SEQUENCE IF EXISTS public."InstitutionStudents_Id_seq";
DROP TABLE IF EXISTS public."InstitutionStudents";
DROP SEQUENCE IF EXISTS public."HomePageFeatures_Id_seq";
DROP TABLE IF EXISTS public."HomePageFeatures";
DROP SEQUENCE IF EXISTS public."Goals_Id_seq";
DROP TABLE IF EXISTS public."Goals";
DROP SEQUENCE IF EXISTS public."ForumVotes_Id_seq";
DROP TABLE IF EXISTS public."ForumVotes";
DROP SEQUENCE IF EXISTS public."ForumThreads_Id_seq";
DROP TABLE IF EXISTS public."ForumThreads";
DROP TABLE IF EXISTS public."ForumThreadFollows";
DROP SEQUENCE IF EXISTS public."ForumPosts_Id_seq";
DROP TABLE IF EXISTS public."ForumPosts";
DROP SEQUENCE IF EXISTS public."ForumModerationQueue_Id_seq";
DROP TABLE IF EXISTS public."ForumModerationQueue";
DROP SEQUENCE IF EXISTS public."FocusSessions_Id_seq";
DROP TABLE IF EXISTS public."FocusSessions";
DROP TABLE IF EXISTS public."Favorites";
DROP SEQUENCE IF EXISTS public."FavoriteCollections_Id_seq";
DROP TABLE IF EXISTS public."FavoriteCollections";
DROP SEQUENCE IF EXISTS public."Exams_Id_seq";
DROP TABLE IF EXISTS public."Exams";
DROP SEQUENCE IF EXISTS public."Events_Id_seq";
DROP TABLE IF EXISTS public."Events";
DROP TABLE IF EXISTS public."Enrollments";
DROP SEQUENCE IF EXISTS public."EmailVerificationTokens_Id_seq";
DROP TABLE IF EXISTS public."EmailVerificationTokens";
DROP TABLE IF EXISTS public."DownloadHistories";
DROP SEQUENCE IF EXISTS public."DirectMessages_Id_seq";
DROP TABLE IF EXISTS public."DirectMessages";
DROP SEQUENCE IF EXISTS public."DeviceInfos_Id_seq";
DROP TABLE IF EXISTS public."DeviceInfos";
DROP SEQUENCE IF EXISTS public."Courses_Id_seq";
DROP TABLE IF EXISTS public."Courses";
DROP SEQUENCE IF EXISTS public."CourseSections_Id_seq";
DROP TABLE IF EXISTS public."CourseSections";
DROP SEQUENCE IF EXISTS public."CourseReviews_Id_seq";
DROP TABLE IF EXISTS public."CourseReviews";
DROP SEQUENCE IF EXISTS public."CourseLessons_Id_seq";
DROP TABLE IF EXISTS public."CourseLessons";
DROP SEQUENCE IF EXISTS public."CourseEnrollments_Id_seq";
DROP TABLE IF EXISTS public."CourseEnrollments";
DROP TABLE IF EXISTS public."CourseContents";
DROP SEQUENCE IF EXISTS public."Conversations_Id_seq";
DROP TABLE IF EXISTS public."Conversations";
DROP SEQUENCE IF EXISTS public."ConcoursEvents_Id_seq";
DROP TABLE IF EXISTS public."ConcoursEvents";
DROP SEQUENCE IF EXISTS public."ChatbotContexts_Id_seq";
DROP TABLE IF EXISTS public."ChatbotContexts";
DROP SEQUENCE IF EXISTS public."Certificates_Id_seq";
DROP TABLE IF EXISTS public."Certificates";
DROP TABLE IF EXISTS public."CartItems";
DROP SEQUENCE IF EXISTS public."BackupCodes_Id_seq";
DROP TABLE IF EXISTS public."BackupCodes";
DROP SEQUENCE IF EXISTS public."ApplicationLogs_Id_seq";
DROP TABLE IF EXISTS public."ApplicationLogs";
DROP SEQUENCE IF EXISTS public."Announcements_Id_seq";
DROP TABLE IF EXISTS public."Announcements";
DROP TABLE IF EXISTS public."AnalyticsEvents";
DROP FUNCTION IF EXISTS public.update_updated_at_column();
DROP FUNCTION IF EXISTS public.sync_enrolled_count();
DROP FUNCTION IF EXISTS public.sync_course_rating();
DROP FUNCTION IF EXISTS public.sync_course_lessons_count();
DROP FUNCTION IF EXISTS public.recalculate_enrollment_progress(p_user_id integer, p_course_id integer);
DROP EXTENSION IF EXISTS pgcrypto;
--
-- Name: pgcrypto; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;


--
-- Name: EXTENSION pgcrypto; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON EXTENSION pgcrypto IS 'cryptographic functions';


--
-- Name: recalculate_enrollment_progress(integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.recalculate_enrollment_progress(p_user_id integer, p_course_id integer) RETURNS void
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_total     INTEGER;
    v_completed INTEGER;
    v_percent   NUMERIC(5,2);
BEGIN
    SELECT COUNT(*) INTO v_total
    FROM "CourseLessons"
    WHERE "CourseId" = p_course_id AND "IsPublished" = TRUE;

    IF v_total = 0 THEN RETURN; END IF;

    SELECT COUNT(*) INTO v_completed
    FROM "LessonProgress"
    WHERE "UserId" = p_user_id
      AND "CourseId" = p_course_id
      AND "IsCompleted" = TRUE;

    v_percent := ROUND((v_completed::NUMERIC / v_total) * 100, 2);

    UPDATE "CourseEnrollments" SET
        "ProgressPercent" = v_percent,
        "CompletedAt"     = CASE
            WHEN v_percent >= 100 AND "CompletedAt" IS NULL THEN NOW()
            ELSE "CompletedAt"
        END,
        "UpdatedAt" = NOW()
    WHERE "UserId" = p_user_id AND "CourseId" = p_course_id;
END;
$$;


--
-- Name: sync_course_lessons_count(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sync_course_lessons_count() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE "Courses" SET "LessonsCount" = (
        SELECT COUNT(*) FROM "CourseLessons"
        WHERE "CourseId" = COALESCE(NEW."CourseId", OLD."CourseId")
          AND "IsPublished" = TRUE
    ) WHERE "Id" = COALESCE(NEW."CourseId", OLD."CourseId");
    RETURN NULL;
END;
$$;


--
-- Name: sync_course_rating(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sync_course_rating() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE v_cid INTEGER;
BEGIN
    v_cid := COALESCE(NEW."CourseId", OLD."CourseId");
    UPDATE "Courses" SET
        "AvgRating"    = COALESCE((SELECT AVG("Rating") FROM "CourseReviews" WHERE "CourseId" = v_cid), 0),
        "ReviewsCount" = (SELECT COUNT(*) FROM "CourseReviews" WHERE "CourseId" = v_cid)
    WHERE "Id" = v_cid;
    RETURN NULL;
END;
$$;


--
-- Name: sync_enrolled_count(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sync_enrolled_count() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE "Courses" SET "EnrolledCount" = (
        SELECT COUNT(*) FROM "CourseEnrollments"
        WHERE "CourseId" = COALESCE(NEW."CourseId", OLD."CourseId")
          AND "IsActive" = TRUE
    ) WHERE "Id" = COALESCE(NEW."CourseId", OLD."CourseId");
    RETURN NULL;
END;
$$;


--
-- Name: update_updated_at_column(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.update_updated_at_column() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    NEW."UpdatedAt" = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: AnalyticsEvents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."AnalyticsEvents" (
    "Id" integer NOT NULL,
    "UserId" integer,
    "EventType" character varying(100) NOT NULL,
    "EventName" character varying(255) NOT NULL,
    "EventCategory" character varying(100),
    "EventData" jsonb,
    "IpAddress" character varying(45),
    "UserAgent" text,
    "CreatedAt" timestamp with time zone NOT NULL
);


--
-- Name: AnalyticsEvents_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."AnalyticsEvents" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."AnalyticsEvents_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Announcements; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Announcements" (
    "Id" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "Content" text,
    "Priority" integer DEFAULT 0,
    "IsPublished" boolean DEFAULT false,
    "PublishedAt" timestamp with time zone,
    "ExpiresAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" integer,
    "IsDeleted" boolean DEFAULT false
);


--
-- Name: Announcements_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Announcements_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Announcements_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Announcements_Id_seq" OWNED BY public."Announcements"."Id";


--
-- Name: ApplicationLogs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ApplicationLogs" (
    "Id" integer NOT NULL,
    "Level" character varying(20) DEFAULT 'Error'::character varying NOT NULL,
    "Category" character varying(200) DEFAULT ''::character varying NOT NULL,
    "Message" text NOT NULL,
    "Exception" text,
    "StackTrace" text,
    "RequestPath" character varying(500),
    "UserId" integer,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "IsResolved" boolean DEFAULT false NOT NULL,
    "ResolvedAt" timestamp with time zone
);


--
-- Name: ApplicationLogs_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."ApplicationLogs_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: ApplicationLogs_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."ApplicationLogs_Id_seq" OWNED BY public."ApplicationLogs"."Id";


--
-- Name: BackupCodes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."BackupCodes" (
    "Id" integer NOT NULL,
    "TwoFactorTokenId" integer NOT NULL,
    "Code" character varying(20) NOT NULL,
    "IsUsed" boolean DEFAULT false NOT NULL,
    "UsedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: BackupCodes_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."BackupCodes_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: BackupCodes_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."BackupCodes_Id_seq" OWNED BY public."BackupCodes"."Id";


--
-- Name: CartItems; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CartItems" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "SubjectId" integer NOT NULL,
    "Price" numeric(10,2) NOT NULL,
    "AddedAt" timestamp with time zone NOT NULL
);


--
-- Name: CartItems_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CartItems" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."CartItems_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Certificates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Certificates" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "EnrollmentId" integer NOT NULL,
    "SubjectId" integer,
    "Title" character varying(255) NOT NULL,
    "CertificateUrl" character varying(500),
    "CertificateNumber" character varying(100),
    "FinalScore" numeric(5,2),
    "IssuedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "CompletionDate" timestamp without time zone,
    "VerificationCode" character varying(100)
);


--
-- Name: Certificates_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Certificates_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Certificates_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Certificates_Id_seq" OWNED BY public."Certificates"."Id";


--
-- Name: ChatbotContexts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ChatbotContexts" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "EducationLevel" character varying(50),
    "Grade" character varying(50),
    "UserObjectives" jsonb,
    "EnrolledSubjects" jsonb,
    "RecentActivity" jsonb,
    "NavigationHistory" jsonb,
    "Preferences" jsonb,
    "Strengths" jsonb,
    "Weaknesses" jsonb,
    "LearningStyle" character varying(50),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "PerformanceHistory" jsonb,
    "ForceLanguage" character varying(10)
);


--
-- Name: ChatbotContexts_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."ChatbotContexts_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: ChatbotContexts_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."ChatbotContexts_Id_seq" OWNED BY public."ChatbotContexts"."Id";


--
-- Name: ConcoursEvents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ConcoursEvents" (
    "Id" integer NOT NULL,
    "Slug" character varying(50) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Year" integer NOT NULL,
    "RegistrationStartDate" timestamp with time zone,
    "RegistrationEndDate" timestamp with time zone,
    "ExamDate" timestamp with time zone,
    "ResultsDate" timestamp with time zone,
    "Location" character varying(300),
    "EnrollmentFeeXaf" integer,
    "OfficialRegistrationUrl" character varying(500),
    "Notes" character varying(1000),
    "IsPublished" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: ConcoursEvents_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."ConcoursEvents_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: ConcoursEvents_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."ConcoursEvents_Id_seq" OWNED BY public."ConcoursEvents"."Id";


--
-- Name: Conversations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Conversations" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Title" character varying(255),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "Tags" jsonb,
    "Metadata" jsonb,
    "LastMessageAt" timestamp without time zone,
    "MessageCount" integer DEFAULT 0 NOT NULL
);


--
-- Name: Conversations_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Conversations_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Conversations_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Conversations_Id_seq" OWNED BY public."Conversations"."Id";


--
-- Name: CourseContents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CourseContents" (
    "Id" integer NOT NULL,
    "SubjectId" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "Description" character varying(2000),
    "VideoUrl" text,
    "DocumentUrl" text,
    "OrderIndex" integer NOT NULL,
    "DurationMinutes" integer NOT NULL,
    "IsLocked" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "CreatedByUserId" integer,
    "Status" character varying(20) DEFAULT 'published'::character varying NOT NULL
);


--
-- Name: CourseContents_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."CourseContents" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."CourseContents_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: CourseEnrollments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CourseEnrollments" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "CourseId" integer NOT NULL,
    "AccessType" text DEFAULT 'free'::text NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "ProgressPercent" numeric(5,2) DEFAULT 0 NOT NULL,
    "EnrolledAt" timestamp with time zone DEFAULT now() NOT NULL,
    "LastAccessedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    "CertificateUrl" text,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: CourseEnrollments_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."CourseEnrollments_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: CourseEnrollments_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."CourseEnrollments_Id_seq" OWNED BY public."CourseEnrollments"."Id";


--
-- Name: CourseLessons; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CourseLessons" (
    "Id" integer NOT NULL,
    "SectionId" integer NOT NULL,
    "CourseId" integer NOT NULL,
    "Title" text NOT NULL,
    "LessonType" text DEFAULT 'video'::text NOT NULL,
    "Description" text,
    "VideoUrl" text,
    "VideoDurationSec" integer DEFAULT 0 NOT NULL,
    "ArticleContent" text,
    "FileUrl" text,
    "FileName" text,
    "Position" integer DEFAULT 0 NOT NULL,
    "IsPreview" boolean DEFAULT false NOT NULL,
    "IsPublished" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: CourseLessons_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."CourseLessons_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: CourseLessons_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."CourseLessons_Id_seq" OWNED BY public."CourseLessons"."Id";


--
-- Name: CourseReviews; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CourseReviews" (
    "Id" integer NOT NULL,
    "CourseId" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Rating" smallint NOT NULL,
    "Comment" text,
    "IsVerified" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "CourseReviews_Rating_check" CHECK ((("Rating" >= 1) AND ("Rating" <= 5)))
);


--
-- Name: CourseReviews_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."CourseReviews_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: CourseReviews_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."CourseReviews_Id_seq" OWNED BY public."CourseReviews"."Id";


--
-- Name: CourseSections; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CourseSections" (
    "Id" integer NOT NULL,
    "CourseId" integer NOT NULL,
    "Title" text NOT NULL,
    "Description" text,
    "Position" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: CourseSections_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."CourseSections_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: CourseSections_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."CourseSections_Id_seq" OWNED BY public."CourseSections"."Id";


--
-- Name: Courses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Courses" (
    "Id" integer NOT NULL,
    "InstructorId" integer NOT NULL,
    "Title" text NOT NULL,
    "Slug" text NOT NULL,
    "Description" text,
    "ShortDescription" text,
    "ThumbnailUrl" text,
    "PreviewVideoUrl" text,
    "Language" text DEFAULT 'fr'::text NOT NULL,
    "Level" text,
    "Category" text,
    "Tags" text[] DEFAULT '{}'::text[] NOT NULL,
    "Price" numeric(12,2) DEFAULT 0 NOT NULL,
    "IsFree" boolean DEFAULT false NOT NULL,
    "IsIncludedInSub" boolean DEFAULT false NOT NULL,
    "Status" text DEFAULT 'draft'::text NOT NULL,
    "Requirements" text[] DEFAULT '{}'::text[] NOT NULL,
    "Objectives" text[] DEFAULT '{}'::text[] NOT NULL,
    "TotalDurationMin" integer DEFAULT 0 NOT NULL,
    "LessonsCount" integer DEFAULT 0 NOT NULL,
    "EnrolledCount" integer DEFAULT 0 NOT NULL,
    "AvgRating" numeric(3,2) DEFAULT 0 NOT NULL,
    "ReviewsCount" integer DEFAULT 0 NOT NULL,
    "CertificateEnabled" boolean DEFAULT true NOT NULL,
    "PublishedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: Courses_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Courses_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Courses_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Courses_Id_seq" OWNED BY public."Courses"."Id";


--
-- Name: DeviceInfos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DeviceInfos" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "DeviceFingerprint" character varying(64) NOT NULL,
    "UserAgent" character varying(500),
    "IpAddress" character varying(45),
    "BrowserName" character varying(50),
    "BrowserVersion" character varying(20),
    "OSName" character varying(50),
    "OSVersion" character varying(20),
    "DeviceName" character varying(50),
    "RememberUntil" timestamp with time zone,
    "LastUsedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: DeviceInfos_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."DeviceInfos_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: DeviceInfos_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."DeviceInfos_Id_seq" OWNED BY public."DeviceInfos"."Id";


--
-- Name: DirectMessages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DirectMessages" (
    "Id" integer NOT NULL,
    "FromUserId" integer NOT NULL,
    "ToUserId" integer NOT NULL,
    "Content" text NOT NULL,
    "IsRead" boolean DEFAULT false NOT NULL,
    "ReadAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "DirectMessages_Content_check" CHECK ((char_length("Content") <= 2000))
);


--
-- Name: DirectMessages_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."DirectMessages_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: DirectMessages_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."DirectMessages_Id_seq" OWNED BY public."DirectMessages"."Id";


--
-- Name: DownloadHistories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DownloadHistories" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "SubjectId" integer NOT NULL,
    "ExamId" integer,
    "FileName" character varying(300),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: DownloadHistories_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DownloadHistories" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DownloadHistories_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: EmailVerificationTokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."EmailVerificationTokens" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "VerificationCode" character varying(6) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "IsVerified" boolean DEFAULT false NOT NULL,
    "AttemptCount" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "VerifiedAt" timestamp with time zone,
    "Purpose" character varying(30) DEFAULT 'verify_email'::character varying NOT NULL
);


--
-- Name: EmailVerificationTokens_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."EmailVerificationTokens_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: EmailVerificationTokens_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."EmailVerificationTokens_Id_seq" OWNED BY public."EmailVerificationTokens"."Id";


--
-- Name: Enrollments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Enrollments" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "SubjectId" integer NOT NULL,
    "EnrolledAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "ProgressPercentage" numeric(5,2) NOT NULL,
    "IsCompleted" boolean NOT NULL,
    "CertificateUrl" text,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


--
-- Name: Enrollments_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Enrollments" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."Enrollments_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Events" (
    "Id" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "Description" text,
    "StartDate" timestamp with time zone NOT NULL,
    "EndDate" timestamp with time zone,
    "Location" character varying(255),
    "EventType" character varying(50),
    "TargetRole" character varying(50),
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone
);


--
-- Name: Events_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Events_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Events_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Events_Id_seq" OWNED BY public."Events"."Id";


--
-- Name: Exams; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Exams" (
    "Id" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "ExamType" character varying(100) NOT NULL,
    "SubjectId" integer,
    "Category" character varying(100) NOT NULL,
    "Year" integer NOT NULL,
    "Session" character varying(50),
    "Level" character varying(100),
    "Duration" integer,
    "DocumentUrl" character varying(500) NOT NULL,
    "CorrectionUrl" character varying(500),
    "Description" text,
    "Difficulty" character varying(50),
    "DownloadCount" integer DEFAULT 0,
    "IsPublished" boolean DEFAULT true,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "IsDeleted" boolean DEFAULT false,
    "ThumbnailUrl" character varying(500)
);


--
-- Name: Exams_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Exams_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Exams_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Exams_Id_seq" OWNED BY public."Exams"."Id";


--
-- Name: FavoriteCollections; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."FavoriteCollections" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Description" character varying(500),
    "Color" character varying(20),
    "Icon" character varying(50),
    "Order" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone
);


--
-- Name: FavoriteCollections_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."FavoriteCollections_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: FavoriteCollections_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."FavoriteCollections_Id_seq" OWNED BY public."FavoriteCollections"."Id";


--
-- Name: Favorites; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Favorites" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "SubjectId" integer NOT NULL,
    "AddedAt" timestamp with time zone NOT NULL,
    "CollectionId" integer
);


--
-- Name: Favorites_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Favorites" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."Favorites_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: FocusSessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."FocusSessions" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "PlannedDurationSeconds" integer DEFAULT 1500 NOT NULL,
    "ActualDurationSeconds" integer,
    "Label" character varying(200),
    "StartedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "CompletedAt" timestamp with time zone
);


--
-- Name: FocusSessions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."FocusSessions_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: FocusSessions_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."FocusSessions_Id_seq" OWNED BY public."FocusSessions"."Id";


--
-- Name: ForumModerationQueue; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ForumModerationQueue" (
    "Id" integer NOT NULL,
    "PostId" integer NOT NULL,
    "ThreadId" integer NOT NULL,
    "ContentText" text NOT NULL,
    "AuthorId" integer,
    "Verdict" character varying(30) NOT NULL,
    "Confidence" numeric(4,2),
    "Reason" text,
    "Status" character varying(20) NOT NULL,
    "CreatedAt" timestamp without time zone NOT NULL,
    "ResolvedAt" timestamp without time zone,
    "AdminNote" text
);


--
-- Name: ForumModerationQueue_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."ForumModerationQueue_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: ForumModerationQueue_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."ForumModerationQueue_Id_seq" OWNED BY public."ForumModerationQueue"."Id";


--
-- Name: ForumPosts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ForumPosts" (
    "Id" integer NOT NULL,
    "ThreadId" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Content" text NOT NULL,
    "Upvotes" integer DEFAULT 0 NOT NULL,
    "IsAccepted" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "IsHidden" boolean DEFAULT false NOT NULL
);


--
-- Name: ForumPosts_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."ForumPosts_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: ForumPosts_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."ForumPosts_Id_seq" OWNED BY public."ForumPosts"."Id";


--
-- Name: ForumThreadFollows; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ForumThreadFollows" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "ThreadId" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: ForumThreadFollows_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."ForumThreadFollows" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."ForumThreadFollows_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: ForumThreads; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ForumThreads" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "Content" text NOT NULL,
    "Category" character varying(100) NOT NULL,
    "Tag" character varying(100),
    "IsPinned" boolean DEFAULT false NOT NULL,
    "IsSolved" boolean DEFAULT false NOT NULL,
    "ViewsCount" integer DEFAULT 0 NOT NULL,
    "RepliesCount" integer DEFAULT 0 NOT NULL,
    "Upvotes" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "IsDeleted" boolean DEFAULT false NOT NULL
);


--
-- Name: ForumThreads_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."ForumThreads_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: ForumThreads_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."ForumThreads_Id_seq" OWNED BY public."ForumThreads"."Id";


--
-- Name: ForumVotes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ForumVotes" (
    "Id" integer NOT NULL,
    "PostId" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Type" character varying(10) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "Weight" integer DEFAULT 1 NOT NULL,
    CONSTRAINT "ForumVotes_Type_check" CHECK ((("Type")::text = ANY ((ARRAY['up'::character varying, 'down'::character varying])::text[])))
);


--
-- Name: ForumVotes_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."ForumVotes_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: ForumVotes_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."ForumVotes_Id_seq" OWNED BY public."ForumVotes"."Id";


--
-- Name: Goals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Goals" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "Description" text,
    "Type" character varying(50) DEFAULT 'academic'::character varying NOT NULL,
    "TargetDate" timestamp with time zone,
    "Status" character varying(50) DEFAULT 'in_progress'::character varying NOT NULL,
    "Progress" numeric(5,2) DEFAULT 0,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: Goals_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Goals_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Goals_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Goals_Id_seq" OWNED BY public."Goals"."Id";


--
-- Name: HomePageFeatures; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."HomePageFeatures" (
    "Id" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "Description" text,
    "Icon" character varying(100),
    "ImageUrl" character varying(500),
    "Order" integer DEFAULT 0 NOT NULL,
    "IsActive" boolean DEFAULT true,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: HomePageFeatures_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."HomePageFeatures_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: HomePageFeatures_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."HomePageFeatures_Id_seq" OWNED BY public."HomePageFeatures"."Id";


--
-- Name: InstitutionStudents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."InstitutionStudents" (
    "Id" integer NOT NULL,
    "InstitutionId" integer NOT NULL,
    "StudentId" integer NOT NULL,
    "GroupName" character varying(100),
    "Level" character varying(100),
    "MatriculeNumber" character varying(50),
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: InstitutionStudents_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."InstitutionStudents_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: InstitutionStudents_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."InstitutionStudents_Id_seq" OWNED BY public."InstitutionStudents"."Id";


--
-- Name: Institutions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Institutions" (
    "Id" integer NOT NULL,
    "Name" character varying(255) NOT NULL,
    "Code" character varying(50),
    "Country" character varying(100) NOT NULL,
    "City" character varying(100),
    "Region" character varying(100),
    "Type" character varying(50),
    "IsActive" boolean DEFAULT true,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean DEFAULT false,
    "Email" character varying(255),
    "Phone" character varying(20),
    "Address" text
);


--
-- Name: Institutions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Institutions_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Institutions_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Institutions_Id_seq" OWNED BY public."Institutions"."Id";


--
-- Name: LearningHistories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."LearningHistories" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "SubjectId" integer NOT NULL,
    "ContentId" integer,
    "ActivityType" character varying(50) NOT NULL,
    "TimeSpentSeconds" integer,
    "QuizScore" numeric(5,2),
    "ActivityAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT '-infinity'::timestamp with time zone NOT NULL,
    "DurationSeconds" integer,
    "EventDescription" text,
    "EventDetails" text,
    "EventTitle" text,
    "EventType" text DEFAULT ''::text NOT NULL,
    "IsCompleted" boolean DEFAULT false NOT NULL,
    "Notes" text,
    "ProgressPercentage" numeric,
    "Score" numeric,
    "UpdatedAt" timestamp with time zone DEFAULT '-infinity'::timestamp with time zone NOT NULL
);


--
-- Name: LearningHistories_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."LearningHistories" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."LearningHistories_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: LessonProgress; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."LessonProgress" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "LessonId" integer NOT NULL,
    "CourseId" integer NOT NULL,
    "IsCompleted" boolean DEFAULT false NOT NULL,
    "WatchTimeSec" integer DEFAULT 0 NOT NULL,
    "LastPositionSec" integer DEFAULT 0 NOT NULL,
    "CompletedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: LessonProgress_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."LessonProgress_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: LessonProgress_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."LessonProgress_Id_seq" OWNED BY public."LessonProgress"."Id";


--
-- Name: Levels; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Levels" (
    "Id" integer NOT NULL,
    "Name" character varying(100) NOT NULL,
    "DisplayName" character varying(150) NOT NULL,
    "Description" text,
    "Order" integer DEFAULT 0 NOT NULL,
    "IsActive" boolean DEFAULT true,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: Levels_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Levels_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Levels_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Levels_Id_seq" OWNED BY public."Levels"."Id";


--
-- Name: Messages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Messages" (
    "Id" integer NOT NULL,
    "ConversationId" integer NOT NULL,
    "Role" character varying(20) NOT NULL,
    "Content" text NOT NULL,
    "TokensUsed" integer,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "IsEdited" boolean DEFAULT false NOT NULL,
    "UpdatedAt" timestamp without time zone,
    "Attachments" jsonb,
    "FeedbackRating" integer,
    "FeedbackComment" character varying(1000),
    "GenerationTimeMs" integer,
    CONSTRAINT "Messages_Role_check" CHECK ((("Role")::text = ANY ((ARRAY['user'::character varying, 'assistant'::character varying])::text[])))
);


--
-- Name: Messages_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Messages_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Messages_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Messages_Id_seq" OWNED BY public."Messages"."Id";


--
-- Name: Notifications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Notifications" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "Message" character varying(2000) NOT NULL,
    "Type" character varying(50) NOT NULL,
    "RelatedEntityType" character varying(50),
    "RelatedEntityId" integer,
    "IsRead" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ReadAt" timestamp with time zone
);


--
-- Name: Notifications_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Notifications" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."Notifications_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: OAuthAccounts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."OAuthAccounts" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Provider" character varying(50) NOT NULL,
    "ProviderUserId" character varying(255) NOT NULL,
    "DisplayName" character varying(255),
    "ProfileImageUrl" character varying(500),
    "Email" character varying(255),
    "ConnectedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "DisconnectedAt" timestamp with time zone
);


--
-- Name: OAuthAccounts_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."OAuthAccounts_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: OAuthAccounts_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."OAuthAccounts_Id_seq" OWNED BY public."OAuthAccounts"."Id";


--
-- Name: OrderItems; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."OrderItems" (
    "Id" integer NOT NULL,
    "OrderId" integer NOT NULL,
    "SubjectId" integer NOT NULL,
    "PriceAtPurchase" numeric(10,2) NOT NULL,
    "CourseId" integer
);


--
-- Name: OrderItems_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."OrderItems" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."OrderItems_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Orders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Orders" (
    "Id" integer NOT NULL,
    "UserId" integer,
    "OrderNumber" character varying(50) NOT NULL,
    "TotalAmount" numeric(12,2) NOT NULL,
    "Status" character varying(50) NOT NULL,
    "PaymentMethod" character varying(50),
    "TransactionId" text,
    "OrderDate" timestamp with time zone NOT NULL,
    "CompletedDate" timestamp with time zone,
    "Notes" text,
    "GuestEmail" character varying(255),
    "GuestName" character varying(255),
    "DiscountAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone
);


--
-- Name: Orders_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Orders" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."Orders_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Pages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Pages" (
    "Id" integer NOT NULL,
    "Slug" character varying(200) NOT NULL,
    "Title" character varying(255) NOT NULL,
    "Content" text,
    "MetaDescription" character varying(500),
    "MetaKeywords" character varying(500),
    "IsPublished" boolean DEFAULT false,
    "PublishedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "CreatedBy" integer,
    "UpdatedBy" integer,
    "Order" integer DEFAULT 0 NOT NULL
);


--
-- Name: Pages_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Pages_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Pages_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Pages_Id_seq" OWNED BY public."Pages"."Id";


--
-- Name: ParentCreditLedgers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ParentCreditLedgers" (
    "Id" integer NOT NULL,
    "ParentId" integer NOT NULL,
    "EntryType" character varying(20) NOT NULL,
    "Amount" numeric(12,2) NOT NULL,
    "ChildId" integer,
    "OrderId" integer,
    "Label" character varying(300),
    "PeriodStart" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: ParentCreditLedgers_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."ParentCreditLedgers_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: ParentCreditLedgers_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."ParentCreditLedgers_Id_seq" OWNED BY public."ParentCreditLedgers"."Id";


--
-- Name: ParentStudentLinks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ParentStudentLinks" (
    "Id" integer NOT NULL,
    "ParentId" integer NOT NULL,
    "StudentId" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: ParentStudentLinks_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."ParentStudentLinks_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: ParentStudentLinks_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."ParentStudentLinks_Id_seq" OWNED BY public."ParentStudentLinks"."Id";


--
-- Name: PasswordResetTokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PasswordResetTokens" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Token" character varying(500) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "IsUsed" boolean DEFAULT false NOT NULL,
    "UsedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: PasswordResetTokens_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."PasswordResetTokens_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: PasswordResetTokens_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."PasswordResetTokens_Id_seq" OWNED BY public."PasswordResetTokens"."Id";


--
-- Name: Payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Payments" (
    "Id" integer NOT NULL,
    "OrderId" integer NOT NULL,
    "UserId" integer,
    "Amount" numeric(10,2) NOT NULL,
    "Currency" character varying(3) DEFAULT 'XAF'::character varying NOT NULL,
    "Status" character varying(50) NOT NULL,
    "PaymentMethod" character varying(100),
    "TransactionId" character varying(255),
    "Description" character varying(500),
    "FeeAmount" numeric,
    "InitiatedAt" timestamp with time zone NOT NULL,
    "ProcessedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    "ErrorMessage" character varying(500),
    "RetryCount" integer,
    "NextRetryAt" timestamp with time zone,
    "Metadata" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "NotchpayReference" character varying(255),
    "PhoneNumber" character varying(20),
    "Operator" character varying(50),
    "ErrorCode" character varying(100),
    "ExpiresAt" timestamp with time zone,
    "GuestEmail" character varying(255)
);


--
-- Name: PlatformAnomalies; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PlatformAnomalies" (
    "Id" integer NOT NULL,
    "Type" character varying(100) NOT NULL,
    "Severity" character varying(20) NOT NULL,
    "Description" text NOT NULL,
    "DetectedAt" timestamp without time zone NOT NULL,
    "ResolvedAt" timestamp without time zone,
    "Resolution" text,
    "Metadata" text
);


--
-- Name: PlatformAnomalies_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."PlatformAnomalies_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: PlatformAnomalies_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."PlatformAnomalies_Id_seq" OWNED BY public."PlatformAnomalies"."Id";


--
-- Name: PricingPlans; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PricingPlans" (
    "Id" integer NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Category" character varying(50) NOT NULL,
    "Price" numeric(10,2) NOT NULL,
    "Period" character varying(50),
    "Features" text,
    "IsPopular" boolean DEFAULT false NOT NULL,
    "Icon" character varying(255),
    "Description" text,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "IsArchived" boolean DEFAULT false NOT NULL,
    "Currency" character varying(3) DEFAULT 'XAF'::character varying NOT NULL,
    "BillingPeriod" character varying(50),
    "MaxDownloads" integer,
    "MaxChatMessages" integer,
    "MonthlyCredits" integer,
    "TeacherRevenueShare" numeric(4,3),
    "MaxChildren" integer
);


--
-- Name: PricingPlans_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."PricingPlans_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: PricingPlans_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."PricingPlans_Id_seq" OWNED BY public."PricingPlans"."Id";


--
-- Name: PromoCodeUsages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PromoCodeUsages" (
    "Id" integer NOT NULL,
    "PromoCodeId" integer NOT NULL,
    "UserId" integer NOT NULL,
    "OrderId" integer NOT NULL,
    "DiscountAmount" numeric(18,2) NOT NULL,
    "UsedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: PromoCodeUsages_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."PromoCodeUsages_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: PromoCodeUsages_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."PromoCodeUsages_Id_seq" OWNED BY public."PromoCodeUsages"."Id";


--
-- Name: PromoCodes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."PromoCodes" (
    "Id" integer NOT NULL,
    "Code" character varying(50) NOT NULL,
    "Description" character varying(500),
    "DiscountType" character varying(20) DEFAULT 'Percentage'::character varying NOT NULL,
    "DiscountValue" numeric(18,2) NOT NULL,
    "MinimumPurchase" numeric(18,2),
    "MaximumDiscount" numeric(18,2),
    "UsageLimit" integer,
    "UsageCount" integer DEFAULT 0 NOT NULL,
    "PerUserLimit" integer DEFAULT 1,
    "ValidFrom" timestamp with time zone DEFAULT now() NOT NULL,
    "ValidUntil" timestamp with time zone,
    "IsActive" boolean DEFAULT true NOT NULL,
    "ApplicableSubjectIds" text,
    "CreatedBy" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone
);


--
-- Name: PromoCodes_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."PromoCodes_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: PromoCodes_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."PromoCodes_Id_seq" OWNED BY public."PromoCodes"."Id";


--
-- Name: Promotions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Promotions" (
    "Id" integer NOT NULL,
    "Code" character varying(50) NOT NULL,
    "DiscountPercent" numeric(5,2) NOT NULL,
    "ValidFrom" timestamp with time zone,
    "ValidUntil" timestamp with time zone,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: Promotions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Promotions_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Promotions_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Promotions_Id_seq" OWNED BY public."Promotions"."Id";


--
-- Name: QuizAttempts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."QuizAttempts" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "QuizId" integer NOT NULL,
    "UserAnswersJson" text DEFAULT '{}'::text NOT NULL,
    "Score" numeric(5,2) DEFAULT 0 NOT NULL,
    "CorrectAnswers" integer DEFAULT 0 NOT NULL,
    "TimeSpentSeconds" integer,
    "Status" character varying(50) DEFAULT 'Submitted'::character varying NOT NULL,
    "IsCompleted" boolean DEFAULT true NOT NULL,
    "Passed" boolean DEFAULT false NOT NULL,
    "AttemptNumber" integer DEFAULT 1 NOT NULL,
    "StartedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "CompletedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: QuizAttempts_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."QuizAttempts_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: QuizAttempts_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."QuizAttempts_Id_seq" OWNED BY public."QuizAttempts"."Id";


--
-- Name: QuizMistakes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."QuizMistakes" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "QuizId" integer,
    "QuizAttemptId" integer,
    "Subject" character varying(100),
    "Question" character varying(1000) NOT NULL,
    "GivenAnswer" character varying(500),
    "CorrectAnswer" character varying(500),
    "IsResolved" boolean DEFAULT false NOT NULL,
    "ResolvedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: QuizMistakes_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."QuizMistakes_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: QuizMistakes_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."QuizMistakes_Id_seq" OWNED BY public."QuizMistakes"."Id";


--
-- Name: Quizzes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Quizzes" (
    "Id" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "SubjectId" integer,
    "CreatedBy" integer,
    "Difficulty" character varying(50) DEFAULT 'medium'::character varying NOT NULL,
    "QuestionCount" integer DEFAULT 10 NOT NULL,
    "TimeLimit" integer,
    "PassingScore" integer DEFAULT 60,
    "Questions" jsonb NOT NULL,
    "IsPublished" boolean DEFAULT true,
    "AttemptCount" integer DEFAULT 0,
    "AverageScore" numeric(5,2) DEFAULT 0,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "IsDeleted" boolean DEFAULT false,
    "Description" character varying(2000),
    "QuestionsJson" text DEFAULT '[]'::text NOT NULL,
    "ExamId" integer,
    "IsAIGenerated" boolean DEFAULT false NOT NULL,
    "Attempts" integer DEFAULT 0 NOT NULL,
    "PassingAttempts" integer DEFAULT 0 NOT NULL,
    "TotalScore" numeric(10,2) DEFAULT 0 NOT NULL,
    "Tags" character varying(1000),
    "PublishedAt" timestamp with time zone
);


--
-- Name: Quizzes_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Quizzes_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Quizzes_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Quizzes_Id_seq" OWNED BY public."Quizzes"."Id";


--
-- Name: RefreshTokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."RefreshTokens" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Token" character varying(500) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "RevokedByIp" character varying(45)
);


--
-- Name: RefreshTokens_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."RefreshTokens_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: RefreshTokens_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."RefreshTokens_Id_seq" OWNED BY public."RefreshTokens"."Id";


--
-- Name: Reviews; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Reviews" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "SubjectId" integer NOT NULL,
    "Rating" integer NOT NULL,
    "Title" character varying(200),
    "Comment" character varying(2000),
    "IsVerifiedPurchase" boolean DEFAULT false NOT NULL,
    "HelpfulCount" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    CONSTRAINT "Reviews_Rating_check" CHECK ((("Rating" >= 1) AND ("Rating" <= 5)))
);


--
-- Name: Reviews_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Reviews_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Reviews_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Reviews_Id_seq" OWNED BY public."Reviews"."Id";


--
-- Name: RevisionEnrollments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."RevisionEnrollments" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "RevisionId" integer NOT NULL,
    "AssociatedLearningHistoryId" integer,
    "OriginalScore" numeric(5,2),
    "Status" character varying(50) DEFAULT 'Assigned'::character varying NOT NULL,
    "ProgressPercentage" numeric(5,2) DEFAULT 0 NOT NULL,
    "IsCompleted" boolean DEFAULT false NOT NULL,
    "FinalScore" numeric(5,2),
    "ScoreImprovement" numeric(5,2),
    "AssignedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone
);


--
-- Name: RevisionEnrollments_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."RevisionEnrollments_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: RevisionEnrollments_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."RevisionEnrollments_Id_seq" OWNED BY public."RevisionEnrollments"."Id";


--
-- Name: RevisionNotes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."RevisionNotes" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "SubjectId" integer NOT NULL,
    "Content" character varying(300) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone
);


--
-- Name: RevisionNotes_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."RevisionNotes_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: RevisionNotes_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."RevisionNotes_Id_seq" OWNED BY public."RevisionNotes"."Id";


--
-- Name: RevisionTags; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."RevisionTags" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "SubjectId" integer NOT NULL,
    "Label" character varying(40) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: RevisionTags_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."RevisionTags_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: RevisionTags_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."RevisionTags_Id_seq" OWNED BY public."RevisionTags"."Id";


--
-- Name: Revisions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Revisions" (
    "Id" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "SubjectId" integer,
    "CreatedBy" integer,
    "Content" text,
    "Summary" text,
    "KeyPoints" jsonb,
    "DocumentUrl" character varying(500),
    "TopicCount" integer DEFAULT 1,
    "Difficulty" character varying(50) DEFAULT 'medium'::character varying,
    "EstimatedDuration" integer,
    "ViewCount" integer DEFAULT 0,
    "IsPublished" boolean DEFAULT true,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "IsDeleted" boolean DEFAULT false,
    "Description" character varying(2000),
    "Subject" character varying(100) DEFAULT ''::character varying NOT NULL,
    "Topic" character varying(100),
    "ExamId" integer,
    "Type" character varying(50) DEFAULT 'Theory'::character varying NOT NULL,
    "VideoUrl" character varying(500),
    "DurationMinutes" integer,
    "CreatedByUserId" integer,
    "Status" character varying(50) DEFAULT 'Available'::character varying NOT NULL,
    "IsAutoAssigned" boolean DEFAULT false NOT NULL,
    "TriggeredByScoreThreshold" integer,
    "ImprovedScore" numeric(5,2),
    "Views" integer DEFAULT 0 NOT NULL,
    "Completions" integer DEFAULT 0 NOT NULL,
    "Tags" character varying(1000),
    "PublishedAt" timestamp with time zone
);


--
-- Name: Revisions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Revisions_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Revisions_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Revisions_Id_seq" OWNED BY public."Revisions"."Id";


--
-- Name: Sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Sessions" (
    "Id" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "Description" text,
    "StartDate" timestamp with time zone NOT NULL,
    "EndDate" timestamp with time zone NOT NULL,
    "MaxParticipants" integer,
    "Status" character varying(50) DEFAULT 'scheduled'::character varying,
    "CreatedBy" integer,
    "SubjectId" integer,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone
);


--
-- Name: Sessions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Sessions_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Sessions_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Sessions_Id_seq" OWNED BY public."Sessions"."Id";


--
-- Name: StudyGroupMembers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."StudyGroupMembers" (
    "Id" integer NOT NULL,
    "StudyGroupId" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Role" character varying(20) DEFAULT 'member'::character varying NOT NULL,
    "JoinedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: StudyGroupMembers_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."StudyGroupMembers_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: StudyGroupMembers_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."StudyGroupMembers_Id_seq" OWNED BY public."StudyGroupMembers"."Id";


--
-- Name: StudyGroups; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."StudyGroups" (
    "Id" integer NOT NULL,
    "OwnerId" integer NOT NULL,
    "Name" character varying(80) NOT NULL,
    "Subject" character varying(100),
    "Description" character varying(500),
    "JoinCode" character varying(10) NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "LastActivityAt" timestamp with time zone
);


--
-- Name: StudyGroups_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."StudyGroups_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: StudyGroups_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."StudyGroups_Id_seq" OWNED BY public."StudyGroups"."Id";


--
-- Name: Subjects; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Subjects" (
    "Id" integer NOT NULL,
    "Title" character varying(255) NOT NULL,
    "Description" character varying(2000),
    "Category" character varying(100),
    "ThumbnailUrl" text,
    "Price" numeric(10,2) NOT NULL,
    "IsPublished" boolean NOT NULL,
    "EnrollmentCount" integer NOT NULL,
    "AverageRating" numeric(3,2) NOT NULL,
    "TotalRatings" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "IsFeatured" boolean DEFAULT false NOT NULL,
    "Tags" jsonb DEFAULT '[]'::jsonb,
    "DownloadCount" integer DEFAULT 0 NOT NULL
);


--
-- Name: Subjects_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Subjects" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."Subjects_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: Subscriptions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Subscriptions" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "PricingPlanId" integer NOT NULL,
    "StartDate" timestamp with time zone NOT NULL,
    "EndDate" timestamp with time zone,
    "Status" character varying(50) DEFAULT 'active'::character varying NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "RenewalCount" integer DEFAULT 0 NOT NULL,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "PlanName" text,
    "TokensUsedThisMonth" integer DEFAULT 0 NOT NULL,
    "TokensResetAt" timestamp without time zone
);


--
-- Name: Subscriptions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Subscriptions_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Subscriptions_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Subscriptions_Id_seq" OWNED BY public."Subscriptions"."Id";


--
-- Name: TeacherClassStudents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeacherClassStudents" (
    "Id" integer NOT NULL,
    "TeacherClassId" integer NOT NULL,
    "StudentId" integer NOT NULL,
    "AddedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: TeacherClassStudents_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."TeacherClassStudents_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: TeacherClassStudents_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."TeacherClassStudents_Id_seq" OWNED BY public."TeacherClassStudents"."Id";


--
-- Name: TeacherClasses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeacherClasses" (
    "Id" integer NOT NULL,
    "TeacherId" integer NOT NULL,
    "Name" character varying(150) NOT NULL,
    "Level" character varying(100),
    "AcademicYear" character varying(20),
    "Description" character varying(500),
    "StudentCount" integer DEFAULT 0 NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone
);


--
-- Name: TeacherClasses_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."TeacherClasses_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: TeacherClasses_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."TeacherClasses_Id_seq" OWNED BY public."TeacherClasses"."Id";


--
-- Name: TeacherStudentLinks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TeacherStudentLinks" (
    "Id" integer NOT NULL,
    "TeacherId" integer NOT NULL,
    "StudentId" integer NOT NULL,
    "Status" character varying(20) DEFAULT 'pending'::character varying NOT NULL,
    "InitiatedBy" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: TeacherStudentLinks_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."TeacherStudentLinks_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: TeacherStudentLinks_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."TeacherStudentLinks_Id_seq" OWNED BY public."TeacherStudentLinks"."Id";


--
-- Name: TwoFactorTokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."TwoFactorTokens" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "TotpSecret" character varying(32),
    "IsTotpEnabled" boolean DEFAULT false NOT NULL,
    "BackupCodesCount" integer DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "UpdatedAt" timestamp with time zone
);


--
-- Name: TwoFactorTokens_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."TwoFactorTokens_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: TwoFactorTokens_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."TwoFactorTokens_Id_seq" OWNED BY public."TwoFactorTokens"."Id";


--
-- Name: UserAIMemories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."UserAIMemories" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "MemoryType" character varying(50) NOT NULL,
    "Content" text NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: UserAIMemories_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."UserAIMemories_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: UserAIMemories_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."UserAIMemories_Id_seq" OWNED BY public."UserAIMemories"."Id";


--
-- Name: UserInterests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."UserInterests" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "SubjectId" integer,
    "Interest" character varying(100),
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: UserInterests_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."UserInterests_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: UserInterests_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."UserInterests_Id_seq" OWNED BY public."UserInterests"."Id";


--
-- Name: UserNotificationSettings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."UserNotificationSettings" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "EmailNotifications" boolean DEFAULT true NOT NULL,
    "PushNotifications" boolean DEFAULT true NOT NULL,
    "CourseCommunity" boolean DEFAULT true NOT NULL,
    "Promotions" boolean DEFAULT false NOT NULL,
    "Newsletters" boolean DEFAULT true NOT NULL,
    "LearningReminders" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "UpdatedAt" timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: UserNotificationSettings_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."UserNotificationSettings_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: UserNotificationSettings_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."UserNotificationSettings_Id_seq" OWNED BY public."UserNotificationSettings"."Id";


--
-- Name: UserPrivacySettings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."UserPrivacySettings" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "ProfileVisible" boolean DEFAULT true NOT NULL,
    "ShowProgressPublic" boolean DEFAULT false NOT NULL,
    "AllowMessages" boolean DEFAULT true NOT NULL,
    "AllowFriends" boolean DEFAULT true NOT NULL,
    "CreatedAt" timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "UpdatedAt" timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: UserPrivacySettings_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."UserPrivacySettings_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: UserPrivacySettings_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."UserPrivacySettings_Id_seq" OWNED BY public."UserPrivacySettings"."Id";


--
-- Name: UserSessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."UserSessions" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "DeviceName" character varying(255),
    "DeviceType" character varying(100),
    "IpAddress" character varying(45),
    "UserAgent" text,
    "Location" character varying(255),
    "RefreshTokenId" integer,
    "CreatedAt" timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "LastActivityAt" timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "ExpiresAt" timestamp without time zone,
    "IsActive" boolean DEFAULT true NOT NULL
);


--
-- Name: UserSessions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."UserSessions_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: UserSessions_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."UserSessions_Id_seq" OWNED BY public."UserSessions"."Id";


--
-- Name: UserTwoFactorAuthentication; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."UserTwoFactorAuthentication" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "IsEnabled" boolean DEFAULT false NOT NULL,
    "Method" character varying(50),
    "TotpSecret" character varying(255),
    "BackupCodes" text,
    "BackupCodesUsed" integer DEFAULT 0 NOT NULL,
    "EnabledAt" timestamp without time zone,
    "LastVerifiedAt" timestamp without time zone,
    "CreatedAt" timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "UpdatedAt" timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: UserTwoFactorAuthentication_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."UserTwoFactorAuthentication_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: UserTwoFactorAuthentication_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."UserTwoFactorAuthentication_Id_seq" OWNED BY public."UserTwoFactorAuthentication"."Id";


--
-- Name: Users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Users" (
    "Id" integer NOT NULL,
    "CognitoId" character varying(255),
    "Email" character varying(255) NOT NULL,
    "FirstName" character varying(100),
    "LastName" character varying(100),
    "ProfileImageUrl" text,
    "Bio" character varying(1000),
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "IsEmailVerified" boolean DEFAULT false NOT NULL,
    "LastLoginAt" timestamp with time zone,
    "PasswordHash" text,
    "Phone" text,
    "Role" text DEFAULT ''::text NOT NULL,
    "VerifiedAt" timestamp with time zone,
    "VerificationCode" text,
    "VerificationCodeExpiredAt" timestamp with time zone,
    "AvatarUrl" text,
    "DeletedBy" text,
    "DeletedByUserId" integer,
    "EmailChangeToken" text,
    "EmailChangeTokenExpiry" timestamp with time zone,
    "PendingEmail" text,
    "IsDeleted" boolean DEFAULT false,
    "EmailVerified" boolean DEFAULT false NOT NULL,
    "Level" character varying(100),
    "City" character varying(100),
    "LastPeriodicConfirmAt" timestamp with time zone,
    "CoverUrl" character varying(500),
    "InstitutionId" integer
);


--
-- Name: Users_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Users" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."Users_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: WebhookIdempotencyKeys; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."WebhookIdempotencyKeys" (
    "Id" integer NOT NULL,
    "Reference" character varying(255) NOT NULL,
    "ProcessedAt" timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: WebhookIdempotencyKeys_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."WebhookIdempotencyKeys_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: WebhookIdempotencyKeys_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."WebhookIdempotencyKeys_Id_seq" OWNED BY public."WebhookIdempotencyKeys"."Id";


--
-- Name: WeeklyGoals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."WeeklyGoals" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "WeekStart" timestamp with time zone NOT NULL,
    "StudyHoursTarget" integer,
    "QuizTarget" integer,
    "DownloadsTarget" integer,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone
);


--
-- Name: WeeklyGoals_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."WeeklyGoals" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."WeeklyGoals_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


--
-- Name: abuse_reports; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.abuse_reports (
    id integer NOT NULL,
    reported_by_user_id integer NOT NULL,
    reported_user_id integer,
    reported_content_id integer,
    reported_content_type character varying(50),
    reason character varying(100),
    description text,
    status character varying(50) DEFAULT 'pending'::character varying,
    action_taken character varying(100),
    notes text,
    resolved_at timestamp without time zone,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: abuse_reports_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.abuse_reports_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: abuse_reports_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.abuse_reports_id_seq OWNED BY public.abuse_reports.id;


--
-- Name: analytics_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.analytics_events (
    id integer NOT NULL,
    user_id integer,
    event_type character varying(50),
    event_name character varying(255),
    event_category character varying(100),
    related_entity_type character varying(50),
    related_entity_id integer,
    event_data jsonb,
    ip_address character varying(45),
    user_agent text,
    session_id character varying(255),
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: analytics_events_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.analytics_events_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: analytics_events_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.analytics_events_id_seq OWNED BY public.analytics_events.id;


--
-- Name: badges; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.badges (
    id integer NOT NULL,
    name character varying(100) NOT NULL,
    description text,
    icon_url character varying(300),
    criteria_type character varying(50),
    criteria_value integer,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: badges_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.badges_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: badges_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.badges_id_seq OWNED BY public.badges.id;


--
-- Name: cohort_analytics; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cohort_analytics (
    id integer NOT NULL,
    cohort_date date,
    cohort_size integer,
    week_1_retention_percentage numeric(5,2),
    week_2_retention_percentage numeric(5,2),
    week_4_retention_percentage numeric(5,2),
    average_rating numeric(3,2),
    completion_rate numeric(5,2),
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: cohort_analytics_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cohort_analytics_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: cohort_analytics_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cohort_analytics_id_seq OWNED BY public.cohort_analytics.id;


--
-- Name: coupons; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coupons (
    id integer NOT NULL,
    code character varying(50) NOT NULL,
    description text,
    discount_type character varying(20) DEFAULT 'percentage'::character varying,
    discount_value numeric(10,2),
    min_purchase numeric(10,2),
    max_uses integer,
    current_uses integer DEFAULT 0,
    applicable_courses character varying(500),
    valid_from timestamp without time zone,
    valid_until timestamp without time zone,
    is_active boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: coupons_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.coupons_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: coupons_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.coupons_id_seq OWNED BY public.coupons.id;


--
-- Name: daily_statistics; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.daily_statistics (
    id integer NOT NULL,
    stat_date date NOT NULL,
    total_users integer DEFAULT 0,
    active_users integer DEFAULT 0,
    new_enrollments integer DEFAULT 0,
    completed_courses integer DEFAULT 0,
    total_revenue numeric(12,2) DEFAULT 0,
    total_watch_hours integer DEFAULT 0,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: daily_statistics_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.daily_statistics_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: daily_statistics_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.daily_statistics_id_seq OWNED BY public.daily_statistics.id;


--
-- Name: features; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.features (
    id integer NOT NULL,
    sprint_id integer,
    title character varying(255) NOT NULL,
    description text,
    type character varying(50),
    priority character varying(50) DEFAULT 'medium'::character varying,
    status character varying(50) DEFAULT 'todo'::character varying,
    story_points integer,
    assigned_to_user_id integer,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: features_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.features_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: features_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.features_id_seq OWNED BY public.features.id;


--
-- Name: notifications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.notifications (
    id integer NOT NULL,
    user_id integer NOT NULL,
    title character varying(255),
    message text,
    notification_type character varying(50),
    related_entity_type character varying(50),
    related_entity_id integer,
    action_url character varying(500),
    is_read boolean DEFAULT false,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    read_at timestamp without time zone
);


--
-- Name: notifications_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.notifications_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: notifications_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.notifications_id_seq OWNED BY public.notifications.id;


--
-- Name: orders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.orders (
    id integer NOT NULL,
    user_id integer NOT NULL,
    order_number character varying(50) NOT NULL,
    total_amount numeric(10,2),
    discount_amount numeric(10,2) DEFAULT 0,
    tax_amount numeric(10,2) DEFAULT 0,
    final_amount numeric(10,2),
    currency character varying(3) DEFAULT 'XAF'::character varying,
    status character varying(50) DEFAULT 'pending'::character varying,
    payment_method character varying(50),
    payment_provider character varying(50),
    transaction_id character varying(255),
    invoice_url character varying(500),
    notes text,
    order_date timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    completed_date timestamp without time zone,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT orders_amount_positive CHECK ((final_amount >= (0)::numeric))
);


--
-- Name: orders_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.orders_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: orders_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.orders_id_seq OWNED BY public.orders.id;


--
-- Name: payments_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."Payments" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."payments_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: refunds; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refunds (
    id integer NOT NULL,
    order_id integer NOT NULL,
    user_id integer NOT NULL,
    reason character varying(255),
    refund_amount numeric(10,2),
    status character varying(50) DEFAULT 'pending'::character varying,
    requested_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    processed_at timestamp without time zone,
    notes text,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: refunds_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.refunds_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: refunds_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.refunds_id_seq OWNED BY public.refunds.id;


--
-- Name: sprints; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sprints (
    id integer NOT NULL,
    name character varying(100),
    start_date date NOT NULL,
    end_date date NOT NULL,
    goal text,
    status character varying(50) DEFAULT 'planning'::character varying,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: sprints_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.sprints_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: sprints_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.sprints_id_seq OWNED BY public.sprints.id;


--
-- Name: user_badges; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_badges (
    id integer NOT NULL,
    user_id integer NOT NULL,
    badge_id integer NOT NULL,
    earned_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: user_badges_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.user_badges_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: user_badges_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.user_badges_id_seq OWNED BY public.user_badges.id;


--
-- Name: user_preferences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_preferences (
    id integer NOT NULL,
    user_id integer NOT NULL,
    notification_email boolean DEFAULT true,
    notification_push boolean DEFAULT true,
    notification_sms boolean DEFAULT false,
    theme_mode character varying(20) DEFAULT 'light'::character varying,
    language_ui character varying(10) DEFAULT 'fr'::character varying,
    auto_play_videos boolean DEFAULT true,
    subtitle_preference character varying(50) DEFAULT 'auto'::character varying,
    marketing_emails boolean DEFAULT false,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: user_preferences_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.user_preferences_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: user_preferences_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.user_preferences_id_seq OWNED BY public.user_preferences.id;


--
-- Name: user_profiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_profiles (
    id integer NOT NULL,
    user_id integer NOT NULL,
    role character varying(50) DEFAULT 'student'::character varying,
    level character varying(50) DEFAULT 'débutant'::character varying,
    learning_goal text,
    specialization character varying(100),
    bio_detailed text,
    avatar_url character varying(500),
    cover_image_url character varying(500),
    total_hours_learning integer DEFAULT 0,
    total_completed_courses integer DEFAULT 0,
    certificates_count integer DEFAULT 0,
    rating numeric(3,2) DEFAULT 0,
    rating_count integer DEFAULT 0,
    is_instructor_verified boolean DEFAULT false,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: user_profiles_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.user_profiles_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: user_profiles_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.user_profiles_id_seq OWNED BY public.user_profiles.id;


--
-- Name: Announcements Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Announcements" ALTER COLUMN "Id" SET DEFAULT nextval('public."Announcements_Id_seq"'::regclass);


--
-- Name: ApplicationLogs Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ApplicationLogs" ALTER COLUMN "Id" SET DEFAULT nextval('public."ApplicationLogs_Id_seq"'::regclass);


--
-- Name: BackupCodes Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BackupCodes" ALTER COLUMN "Id" SET DEFAULT nextval('public."BackupCodes_Id_seq"'::regclass);


--
-- Name: Certificates Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Certificates" ALTER COLUMN "Id" SET DEFAULT nextval('public."Certificates_Id_seq"'::regclass);


--
-- Name: ChatbotContexts Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ChatbotContexts" ALTER COLUMN "Id" SET DEFAULT nextval('public."ChatbotContexts_Id_seq"'::regclass);


--
-- Name: ConcoursEvents Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ConcoursEvents" ALTER COLUMN "Id" SET DEFAULT nextval('public."ConcoursEvents_Id_seq"'::regclass);


--
-- Name: Conversations Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Conversations" ALTER COLUMN "Id" SET DEFAULT nextval('public."Conversations_Id_seq"'::regclass);


--
-- Name: CourseEnrollments Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseEnrollments" ALTER COLUMN "Id" SET DEFAULT nextval('public."CourseEnrollments_Id_seq"'::regclass);


--
-- Name: CourseLessons Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseLessons" ALTER COLUMN "Id" SET DEFAULT nextval('public."CourseLessons_Id_seq"'::regclass);


--
-- Name: CourseReviews Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseReviews" ALTER COLUMN "Id" SET DEFAULT nextval('public."CourseReviews_Id_seq"'::regclass);


--
-- Name: CourseSections Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseSections" ALTER COLUMN "Id" SET DEFAULT nextval('public."CourseSections_Id_seq"'::regclass);


--
-- Name: Courses Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Courses" ALTER COLUMN "Id" SET DEFAULT nextval('public."Courses_Id_seq"'::regclass);


--
-- Name: DeviceInfos Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DeviceInfos" ALTER COLUMN "Id" SET DEFAULT nextval('public."DeviceInfos_Id_seq"'::regclass);


--
-- Name: DirectMessages Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DirectMessages" ALTER COLUMN "Id" SET DEFAULT nextval('public."DirectMessages_Id_seq"'::regclass);


--
-- Name: EmailVerificationTokens Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."EmailVerificationTokens" ALTER COLUMN "Id" SET DEFAULT nextval('public."EmailVerificationTokens_Id_seq"'::regclass);


--
-- Name: Events Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Events" ALTER COLUMN "Id" SET DEFAULT nextval('public."Events_Id_seq"'::regclass);


--
-- Name: Exams Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Exams" ALTER COLUMN "Id" SET DEFAULT nextval('public."Exams_Id_seq"'::regclass);


--
-- Name: FavoriteCollections Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FavoriteCollections" ALTER COLUMN "Id" SET DEFAULT nextval('public."FavoriteCollections_Id_seq"'::regclass);


--
-- Name: FocusSessions Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FocusSessions" ALTER COLUMN "Id" SET DEFAULT nextval('public."FocusSessions_Id_seq"'::regclass);


--
-- Name: ForumModerationQueue Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumModerationQueue" ALTER COLUMN "Id" SET DEFAULT nextval('public."ForumModerationQueue_Id_seq"'::regclass);


--
-- Name: ForumPosts Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumPosts" ALTER COLUMN "Id" SET DEFAULT nextval('public."ForumPosts_Id_seq"'::regclass);


--
-- Name: ForumThreads Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumThreads" ALTER COLUMN "Id" SET DEFAULT nextval('public."ForumThreads_Id_seq"'::regclass);


--
-- Name: ForumVotes Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumVotes" ALTER COLUMN "Id" SET DEFAULT nextval('public."ForumVotes_Id_seq"'::regclass);


--
-- Name: Goals Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Goals" ALTER COLUMN "Id" SET DEFAULT nextval('public."Goals_Id_seq"'::regclass);


--
-- Name: HomePageFeatures Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."HomePageFeatures" ALTER COLUMN "Id" SET DEFAULT nextval('public."HomePageFeatures_Id_seq"'::regclass);


--
-- Name: InstitutionStudents Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."InstitutionStudents" ALTER COLUMN "Id" SET DEFAULT nextval('public."InstitutionStudents_Id_seq"'::regclass);


--
-- Name: Institutions Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Institutions" ALTER COLUMN "Id" SET DEFAULT nextval('public."Institutions_Id_seq"'::regclass);


--
-- Name: LessonProgress Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."LessonProgress" ALTER COLUMN "Id" SET DEFAULT nextval('public."LessonProgress_Id_seq"'::regclass);


--
-- Name: Levels Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Levels" ALTER COLUMN "Id" SET DEFAULT nextval('public."Levels_Id_seq"'::regclass);


--
-- Name: Messages Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Messages" ALTER COLUMN "Id" SET DEFAULT nextval('public."Messages_Id_seq"'::regclass);


--
-- Name: OAuthAccounts Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OAuthAccounts" ALTER COLUMN "Id" SET DEFAULT nextval('public."OAuthAccounts_Id_seq"'::regclass);


--
-- Name: Pages Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Pages" ALTER COLUMN "Id" SET DEFAULT nextval('public."Pages_Id_seq"'::regclass);


--
-- Name: ParentCreditLedgers Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ParentCreditLedgers" ALTER COLUMN "Id" SET DEFAULT nextval('public."ParentCreditLedgers_Id_seq"'::regclass);


--
-- Name: ParentStudentLinks Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ParentStudentLinks" ALTER COLUMN "Id" SET DEFAULT nextval('public."ParentStudentLinks_Id_seq"'::regclass);


--
-- Name: PasswordResetTokens Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PasswordResetTokens" ALTER COLUMN "Id" SET DEFAULT nextval('public."PasswordResetTokens_Id_seq"'::regclass);


--
-- Name: PlatformAnomalies Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PlatformAnomalies" ALTER COLUMN "Id" SET DEFAULT nextval('public."PlatformAnomalies_Id_seq"'::regclass);


--
-- Name: PricingPlans Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PricingPlans" ALTER COLUMN "Id" SET DEFAULT nextval('public."PricingPlans_Id_seq"'::regclass);


--
-- Name: PromoCodeUsages Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodeUsages" ALTER COLUMN "Id" SET DEFAULT nextval('public."PromoCodeUsages_Id_seq"'::regclass);


--
-- Name: PromoCodes Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodes" ALTER COLUMN "Id" SET DEFAULT nextval('public."PromoCodes_Id_seq"'::regclass);


--
-- Name: Promotions Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Promotions" ALTER COLUMN "Id" SET DEFAULT nextval('public."Promotions_Id_seq"'::regclass);


--
-- Name: QuizAttempts Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."QuizAttempts" ALTER COLUMN "Id" SET DEFAULT nextval('public."QuizAttempts_Id_seq"'::regclass);


--
-- Name: QuizMistakes Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."QuizMistakes" ALTER COLUMN "Id" SET DEFAULT nextval('public."QuizMistakes_Id_seq"'::regclass);


--
-- Name: Quizzes Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Quizzes" ALTER COLUMN "Id" SET DEFAULT nextval('public."Quizzes_Id_seq"'::regclass);


--
-- Name: RefreshTokens Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RefreshTokens" ALTER COLUMN "Id" SET DEFAULT nextval('public."RefreshTokens_Id_seq"'::regclass);


--
-- Name: Reviews Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Reviews" ALTER COLUMN "Id" SET DEFAULT nextval('public."Reviews_Id_seq"'::regclass);


--
-- Name: RevisionEnrollments Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionEnrollments" ALTER COLUMN "Id" SET DEFAULT nextval('public."RevisionEnrollments_Id_seq"'::regclass);


--
-- Name: RevisionNotes Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionNotes" ALTER COLUMN "Id" SET DEFAULT nextval('public."RevisionNotes_Id_seq"'::regclass);


--
-- Name: RevisionTags Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionTags" ALTER COLUMN "Id" SET DEFAULT nextval('public."RevisionTags_Id_seq"'::regclass);


--
-- Name: Revisions Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Revisions" ALTER COLUMN "Id" SET DEFAULT nextval('public."Revisions_Id_seq"'::regclass);


--
-- Name: Sessions Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Sessions" ALTER COLUMN "Id" SET DEFAULT nextval('public."Sessions_Id_seq"'::regclass);


--
-- Name: StudyGroupMembers Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."StudyGroupMembers" ALTER COLUMN "Id" SET DEFAULT nextval('public."StudyGroupMembers_Id_seq"'::regclass);


--
-- Name: StudyGroups Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."StudyGroups" ALTER COLUMN "Id" SET DEFAULT nextval('public."StudyGroups_Id_seq"'::regclass);


--
-- Name: Subscriptions Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Subscriptions" ALTER COLUMN "Id" SET DEFAULT nextval('public."Subscriptions_Id_seq"'::regclass);


--
-- Name: TeacherClassStudents Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherClassStudents" ALTER COLUMN "Id" SET DEFAULT nextval('public."TeacherClassStudents_Id_seq"'::regclass);


--
-- Name: TeacherClasses Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherClasses" ALTER COLUMN "Id" SET DEFAULT nextval('public."TeacherClasses_Id_seq"'::regclass);


--
-- Name: TeacherStudentLinks Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherStudentLinks" ALTER COLUMN "Id" SET DEFAULT nextval('public."TeacherStudentLinks_Id_seq"'::regclass);


--
-- Name: TwoFactorTokens Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TwoFactorTokens" ALTER COLUMN "Id" SET DEFAULT nextval('public."TwoFactorTokens_Id_seq"'::regclass);


--
-- Name: UserAIMemories Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserAIMemories" ALTER COLUMN "Id" SET DEFAULT nextval('public."UserAIMemories_Id_seq"'::regclass);


--
-- Name: UserInterests Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserInterests" ALTER COLUMN "Id" SET DEFAULT nextval('public."UserInterests_Id_seq"'::regclass);


--
-- Name: UserNotificationSettings Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserNotificationSettings" ALTER COLUMN "Id" SET DEFAULT nextval('public."UserNotificationSettings_Id_seq"'::regclass);


--
-- Name: UserPrivacySettings Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserPrivacySettings" ALTER COLUMN "Id" SET DEFAULT nextval('public."UserPrivacySettings_Id_seq"'::regclass);


--
-- Name: UserSessions Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserSessions" ALTER COLUMN "Id" SET DEFAULT nextval('public."UserSessions_Id_seq"'::regclass);


--
-- Name: UserTwoFactorAuthentication Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserTwoFactorAuthentication" ALTER COLUMN "Id" SET DEFAULT nextval('public."UserTwoFactorAuthentication_Id_seq"'::regclass);


--
-- Name: WebhookIdempotencyKeys Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."WebhookIdempotencyKeys" ALTER COLUMN "Id" SET DEFAULT nextval('public."WebhookIdempotencyKeys_Id_seq"'::regclass);


--
-- Name: abuse_reports id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.abuse_reports ALTER COLUMN id SET DEFAULT nextval('public.abuse_reports_id_seq'::regclass);


--
-- Name: analytics_events id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.analytics_events ALTER COLUMN id SET DEFAULT nextval('public.analytics_events_id_seq'::regclass);


--
-- Name: badges id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.badges ALTER COLUMN id SET DEFAULT nextval('public.badges_id_seq'::regclass);


--
-- Name: cohort_analytics id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cohort_analytics ALTER COLUMN id SET DEFAULT nextval('public.cohort_analytics_id_seq'::regclass);


--
-- Name: coupons id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coupons ALTER COLUMN id SET DEFAULT nextval('public.coupons_id_seq'::regclass);


--
-- Name: daily_statistics id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.daily_statistics ALTER COLUMN id SET DEFAULT nextval('public.daily_statistics_id_seq'::regclass);


--
-- Name: features id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.features ALTER COLUMN id SET DEFAULT nextval('public.features_id_seq'::regclass);


--
-- Name: notifications id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notifications ALTER COLUMN id SET DEFAULT nextval('public.notifications_id_seq'::regclass);


--
-- Name: orders id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders ALTER COLUMN id SET DEFAULT nextval('public.orders_id_seq'::regclass);


--
-- Name: refunds id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refunds ALTER COLUMN id SET DEFAULT nextval('public.refunds_id_seq'::regclass);


--
-- Name: sprints id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sprints ALTER COLUMN id SET DEFAULT nextval('public.sprints_id_seq'::regclass);


--
-- Name: user_badges id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_badges ALTER COLUMN id SET DEFAULT nextval('public.user_badges_id_seq'::regclass);


--
-- Name: user_preferences id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_preferences ALTER COLUMN id SET DEFAULT nextval('public.user_preferences_id_seq'::regclass);


--
-- Name: user_profiles id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_profiles ALTER COLUMN id SET DEFAULT nextval('public.user_profiles_id_seq'::regclass);


--
-- Data for Name: AnalyticsEvents; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."AnalyticsEvents" ("Id", "UserId", "EventType", "EventName", "EventCategory", "EventData", "IpAddress", "UserAgent", "CreatedAt") FROM stdin;
1	\N	signup_attempt	signup_attempt	\N	\N	\N	\N	2026-02-18 12:58:47.707877+00
2	\N	login_attempt	login_attempt	\N	\N	\N	\N	2026-02-19 16:39:33.768058+00
3	\N	login_success	login_success	\N	\N	\N	\N	2026-02-19 16:39:35.226684+00
4	\N	login_attempt	login_attempt	\N	\N	\N	\N	2026-02-19 16:46:44.184546+00
5	\N	login_success	login_success	\N	\N	\N	\N	2026-02-19 16:46:45.805813+00
6	\N	signup_attempt	signup_attempt	\N	\N	\N	\N	2026-04-30 14:52:23.828013+00
7	\N	signup_success	signup_success	\N	\N	\N	\N	2026-04-30 14:52:24.964826+00
\.


--
-- Data for Name: Announcements; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Announcements" ("Id", "Title", "Content", "Priority", "IsPublished", "PublishedAt", "ExpiresAt", "CreatedAt", "UpdatedAt", "CreatedBy", "IsDeleted") FROM stdin;
1	Bienvenue sur WinPlus !	Découvrez notre plateforme éducative avec plus de 30 cours disponibles. Inscrivez-vous et commencez à apprendre dès aujourd'hui !	2	t	2026-02-01 08:00:00+00	\N	2026-02-18 14:16:46.900007+00	\N	1	f
2	Nouveaux cours informatiques disponibles	Nous avons ajouté React & Next.js et JavaScript Avancé à notre catalogue. Profitez-en !	1	t	2026-02-10 10:00:00+00	\N	2026-02-18 14:16:46.900007+00	\N	3	f
3	Promotion de février - 20% sur tous les cours	Utilisez le code FEVRIER2026 pour bénéficier de 20% de réduction sur tous les cours jusqu'au 28 février.	3	t	2026-02-15 09:00:00+00	\N	2026-02-18 14:16:46.900007+00	\N	1	f
4	Maintenance prévue le 22 février	La plateforme sera indisponible le 22 février de 02h à 06h pour une mise à jour. Merci de votre compréhension.	2	t	2026-02-17 14:00:00+00	\N	2026-02-18 14:16:46.900007+00	\N	1	f
5	Sessions de révision pour le Bac 2026	Des sessions live de révision pour le Baccalauréat commencent dès mars. Inscriptions ouvertes !	1	t	2026-02-18 08:00:00+00	\N	2026-02-18 14:16:46.900007+00	\N	3	f
\.


--
-- Data for Name: ApplicationLogs; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ApplicationLogs" ("Id", "Level", "Category", "Message", "Exception", "StackTrace", "RequestPath", "UserId", "CreatedAt", "IsResolved", "ResolvedAt") FROM stdin;
1	Error	DbUpdateException	An error occurred while saving the entity changes. See the inner exception for details.	Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.\n ---> Npgsql.PostgresException (0x80004005): 42703: column "IsActive" of relation "Conversations" does not exist\n\nPOSITION: 43\n   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n  Exception data:\n    Severity: ERROR\n    SqlState: 42703\n    MessageText: column "IsActive" of relation "Conversations" does not exist\n    Position: 43\n    File: parse_target.c\n    Line: 1075\n    Routine: checkInsertTargets\n   --- End of inner exception stack trace ---\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 357\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 357\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	/api/chatbot/stream	\N	2026-06-22 13:50:05.845901+00	f	\N
2	Error	InvalidOperationException	Headers are read-only, response has already started.	System.InvalidOperationException: Headers are read-only, response has already started.\n   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpHeaders.ThrowHeadersReadOnlyException()\n   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpResponseHeaders.Microsoft.AspNetCore.Http.IHeaderDictionary.set_Allow(StringValues value)\n   at Microsoft.AspNetCore.Routing.Matching.HttpMethodMatcherPolicy.<>c__DisplayClass13_0.<CreateRejectionEndpoint>b__0(HttpContext context)\n   at Microsoft.AspNetCore.Routing.EndpointMiddleware.Invoke(HttpContext httpContext)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpHeaders.ThrowHeadersReadOnlyException()\n   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpResponseHeaders.Microsoft.AspNetCore.Http.IHeaderDictionary.set_Allow(StringValues value)\n   at Microsoft.AspNetCore.Routing.Matching.HttpMethodMatcherPolicy.<>c__DisplayClass13_0.<CreateRejectionEndpoint>b__0(HttpContext context)\n   at Microsoft.AspNetCore.Routing.EndpointMiddleware.Invoke(HttpContext httpContext)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	/api/users/me	\N	2026-06-22 15:58:42.975576+00	f	\N
3	Error	DbUpdateException	An error occurred while saving the entity changes. See the inner exception for details.	Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.\n ---> Npgsql.PostgresException (0x80004005): 42703: column "IsActive" of relation "Conversations" does not exist\n\nPOSITION: 43\n   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n  Exception data:\n    Severity: ERROR\n    SqlState: 42703\n    MessageText: column "IsActive" of relation "Conversations" does not exist\n    Position: 43\n    File: parse_target.c\n    Line: 1075\n    Routine: checkInsertTargets\n   --- End of inner exception stack trace ---\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 357\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 357\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	/api/chatbot/stream	\N	2026-06-23 14:49:10.14436+00	f	\N
4	Error	InvalidOperationException	Headers are read-only, response has already started.	System.InvalidOperationException: Headers are read-only, response has already started.\n   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpHeaders.ThrowHeadersReadOnlyException()\n   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpResponseHeaders.Microsoft.AspNetCore.Http.IHeaderDictionary.set_Allow(StringValues value)\n   at Microsoft.AspNetCore.Routing.Matching.HttpMethodMatcherPolicy.<>c__DisplayClass13_0.<CreateRejectionEndpoint>b__0(HttpContext context)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpHeaders.ThrowHeadersReadOnlyException()\n   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpResponseHeaders.Microsoft.AspNetCore.Http.IHeaderDictionary.set_Allow(StringValues value)\n   at Microsoft.AspNetCore.Routing.Matching.HttpMethodMatcherPolicy.<>c__DisplayClass13_0.<CreateRejectionEndpoint>b__0(HttpContext context)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	/api/users/me	\N	2026-06-29 16:23:15.955844+00	f	\N
5	Error	InvalidOperationException	StatusCode cannot be set because the response has already started.	System.InvalidOperationException: StatusCode cannot be set because the response has already started.\n   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpProtocol.ThrowResponseAlreadyStartedException(String value)\n   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpProtocol.set_StatusCode(Int32 value)\n   at Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerHandler.HandleChallengeAsync(AuthenticationProperties properties)\n   at Microsoft.AspNetCore.Authentication.AuthenticationHandler`1.ChallengeAsync(AuthenticationProperties properties)\n   at Microsoft.AspNetCore.Authentication.AuthenticationService.ChallengeAsync(HttpContext context, String scheme, AuthenticationProperties properties)\n   at Microsoft.AspNetCore.Authorization.Policy.AuthorizationMiddlewareResultHandler.<>c__DisplayClass0_0.<<HandleAsync>g__Handle|0>d.MoveNext()\n--- End of stack trace from previous location ---\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpProtocol.ThrowResponseAlreadyStartedException(String value)\n   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpProtocol.set_StatusCode(Int32 value)\n   at Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerHandler.HandleChallengeAsync(AuthenticationProperties properties)\n   at Microsoft.AspNetCore.Authentication.AuthenticationHandler`1.ChallengeAsync(AuthenticationProperties properties)\n   at Microsoft.AspNetCore.Authentication.AuthenticationService.ChallengeAsync(HttpContext context, String scheme, AuthenticationProperties properties)\n   at Microsoft.AspNetCore.Authorization.Policy.AuthorizationMiddlewareResultHandler.<>c__DisplayClass0_0.<<HandleAsync>g__Handle|0>d.MoveNext()\n--- End of stack trace from previous location ---\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	/api/notifications	\N	2026-06-29 16:23:15.957021+00	f	\N
6	Error	InvalidOperationException	Unable to resolve service for type 'Backend.Services.IAIService' while attempting to activate 'Backend.Controllers.AIController'.	System.InvalidOperationException: Unable to resolve service for type 'Backend.Services.IAIService' while attempting to activate 'Backend.Controllers.AIController'.\n   at Microsoft.Extensions.DependencyInjection.ActivatorUtilities.ThrowHelperUnableToResolveService(Type type, Type requiredBy)\n   at lambda_method164(Closure, IServiceProvider, Object[])\n   at Microsoft.AspNetCore.Mvc.Controllers.ControllerFactoryProvider.<>c__DisplayClass6_0.<CreateControllerFactory>g__CreateController|0(ControllerContext controllerContext)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.InvokeInnerFilterAsync()\n--- End of stack trace from previous location ---\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Microsoft.Extensions.DependencyInjection.ActivatorUtilities.ThrowHelperUnableToResolveService(Type type, Type requiredBy)\n   at lambda_method164(Closure, IServiceProvider, Object[])\n   at Microsoft.AspNetCore.Mvc.Controllers.ControllerFactoryProvider.<>c__DisplayClass6_0.<CreateControllerFactory>g__CreateController|0(ControllerContext controllerContext)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.InvokeInnerFilterAsync()\n--- End of stack trace from previous location ---\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	/api/ai/health	\N	2026-08-06 22:10:44.583688+00	f	\N
7	Error	JsonException	A possible object cycle was detected. This can either be due to a cycle or if the object depth is larger than the maximum allowed depth of 32. Consider using ReferenceHandler.Preserve on JsonSerializerOptions to support cycles. Path: $.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.	System.Text.Json.JsonException: A possible object cycle was detected. This can either be due to a cycle or if the object depth is larger than the maximum allowed depth of 32. Consider using ReferenceHandler.Preserve on JsonSerializerOptions to support cycles. Path: $.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.Order.Items.\n   at System.Text.Json.ThrowHelper.ThrowJsonException_SerializerCycleDetected(Int32 maxDepth)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Converters.IEnumerableDefaultConverter`2.OnWriteResume(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonCollectionConverter`2.OnTryWrite(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Converters.IEnumerableDefaultConverter`2.OnWriteResume(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonCollectionConverter`2.OnTryWrite(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Converters.IEnumerableDefaultConverter`2.OnWriteResume(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonCollectionConverter`2.OnTryWrite(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Converters.IEnumerableDefaultConverter`2.OnWriteResume(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonCollectionConverter`2.OnTryWrite(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Converters.IEnumerableDefaultConverter`2.OnWriteResume(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonCollectionConverter`2.OnTryWrite(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Converters.IEnumerableDefaultConverter`2.OnWriteResume(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonCollectionConverter`2.OnTryWrite(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Jso	   at System.Text.Json.ThrowHelper.ThrowJsonException_SerializerCycleDetected(Int32 maxDepth)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Converters.IEnumerableDefaultConverter`2.OnWriteResume(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonCollectionConverter`2.OnTryWrite(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Converters.IEnumerableDefaultConverter`2.OnWriteResume(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonCollectionConverter`2.OnTryWrite(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Converters.IEnumerableDefaultConverter`2.OnWriteResume(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonCollectionConverter`2.OnTryWrite(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1.GetMemberAndWriteJson(Object obj, WriteStack& state, Utf8JsonWriter writer)\n   at System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1.OnTryWrite(Utf8JsonWriter writer, T value, JsonSerializerOptions options, WriteStack& state)\n   at System.Text.Json.Serialization.JsonConverter`1.TryWrite(Utf8JsonWriter writer, T& value, JsonSerializerOptions options, WriteStack& state)\n   at System.Tex	/api/orders	\N	2026-08-07 04:06:24.207305+00	f	\N
8	Error	InvalidOperationException	JsonPropertyInfo 'order' defined in type 'Backend.Models.Entities.OrderItem' is marked required but does not specify a setter.	System.InvalidOperationException: JsonPropertyInfo 'order' defined in type 'Backend.Models.Entities.OrderItem' is marked required but does not specify a setter.\n   at System.Text.Json.ThrowHelper.ThrowInvalidOperationException_JsonPropertyRequiredAndNotDeserializable(JsonPropertyInfo jsonPropertyInfo)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo.Configure()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.ConfigureProperties()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.Configure()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.<EnsureConfigured>g__ConfigureSynchronized|172_0()\n   at System.Text.Json.JsonSerializerOptions.GetTypeInfoInternal(Type type, Boolean ensureConfigured, Nullable`1 ensureNotNull, Boolean resolveIfMutable, Boolean fallBackToNearestAncestorType)\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.Configure()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.<EnsureConfigured>g__ConfigureSynchronized|172_0()\n   at System.Text.Json.JsonSerializerOptions.GetTypeInfoInternal(Type type, Boolean ensureConfigured, Nullable`1 ensureNotNull, Boolean resolveIfMutable, Boolean fallBackToNearestAncestorType)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo.Configure()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.ConfigureProperties()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.Configure()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.<EnsureConfigured>g__ConfigureSynchronized|172_0()\n   at System.Text.Json.JsonSerializerOptions.GetTypeInfoInternal(Type type, Boolean ensureConfigured, Nullable`1 ensureNotNull, Boolean resolveIfMutable, Boolean fallBackToNearestAncestorType)\n   at Microsoft.AspNetCore.Mvc.Formatters.SystemTextJsonOutputFormatter.WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeNextResultFilterAsync>g__Awaited|30_0[TFilter,TFilterAsync](ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.Rethrow(ResultExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.ResultNext[TFilter,TFilterAsync](State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.InvokeResultFilters()\n--- End of stack trace from previous location ---\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at System.Text.Json.ThrowHelper.ThrowInvalidOperationException_JsonPropertyRequiredAndNotDeserializable(JsonPropertyInfo jsonPropertyInfo)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo.Configure()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.ConfigureProperties()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.Configure()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.<EnsureConfigured>g__ConfigureSynchronized|172_0()\n   at System.Text.Json.JsonSerializerOptions.GetTypeInfoInternal(Type type, Boolean ensureConfigured, Nullable`1 ensureNotNull, Boolean resolveIfMutable, Boolean fallBackToNearestAncestorType)\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.Configure()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.<EnsureConfigured>g__ConfigureSynchronized|172_0()\n   at System.Text.Json.JsonSerializerOptions.GetTypeInfoInternal(Type type, Boolean ensureConfigured, Nullable`1 ensureNotNull, Boolean resolveIfMutable, Boolean fallBackToNearestAncestorType)\n   at System.Text.Json.Serialization.Metadata.JsonPropertyInfo.Configure()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.ConfigureProperties()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.Configure()\n   at System.Text.Json.Serialization.Metadata.JsonTypeInfo.<EnsureConfigured>g__ConfigureSynchronized|172_0()\n   at System.Text.Json.JsonSerializerOptions.GetTypeInfoInternal(Type type, Boolean ensureConfigured, Nullable`1 ensureNotNull, Boolean resolveIfMutable, Boolean fallBackToNearestAncestorType)\n   at Microsoft.AspNetCore.Mvc.Formatters.SystemTextJsonOutputFormatter.WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeNextResultFilterAsync>g__Awaited|30_0[TFilter,TFilterAsync](ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.Rethrow(ResultExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.ResultNext[TFilter,TFilterAsync](State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.InvokeResultFilters()\n--- End of stack trace from previous location ---\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	/api/orders	\N	2026-08-07 21:22:05.645919+00	f	\N
9	Error	DbUpdateException	An error occurred while saving the entity changes. See the inner exception for details.	Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.\n ---> Npgsql.PostgresException (0x80004005): 42703: column "IsActive" of relation "Conversations" does not exist\n\nPOSITION: 43\n   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n  Exception data:\n    Severity: ERROR\n    SqlState: 42703\n    MessageText: column "IsActive" of relation "Conversations" does not exist\n    Position: 43\n    File: parse_target.c\n    Line: 1075\n    Routine: checkInsertTargets\n   --- End of inner exception stack trace ---\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 357\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 357\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	/api/chatbot/stream	\N	2026-08-20 06:56:02.117273+00	f	\N
10	Error	InvalidOperationException	Unable to resolve service for type 'Backend.Services.IFavoriteService' while attempting to activate 'Backend.Controllers.FavoritesController'.	System.InvalidOperationException: Unable to resolve service for type 'Backend.Services.IFavoriteService' while attempting to activate 'Backend.Controllers.FavoritesController'.\n   at Microsoft.Extensions.DependencyInjection.ActivatorUtilities.ThrowHelperUnableToResolveService(Type type, Type requiredBy)\n   at lambda_method1397(Closure, IServiceProvider, Object[])\n   at Microsoft.AspNetCore.Mvc.Controllers.ControllerFactoryProvider.<>c__DisplayClass6_0.<CreateControllerFactory>g__CreateController|0(ControllerContext controllerContext)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.InvokeInnerFilterAsync()\n--- End of stack trace from previous location ---\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Microsoft.Extensions.DependencyInjection.ActivatorUtilities.ThrowHelperUnableToResolveService(Type type, Type requiredBy)\n   at lambda_method1397(Closure, IServiceProvider, Object[])\n   at Microsoft.AspNetCore.Mvc.Controllers.ControllerFactoryProvider.<>c__DisplayClass6_0.<CreateControllerFactory>g__CreateController|0(ControllerContext controllerContext)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.InvokeInnerFilterAsync()\n--- End of stack trace from previous location ---\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	/api/favorites/30	\N	2026-08-20 07:03:30.942391+00	f	\N
11	Error	DbUpdateException	An error occurred while saving the entity changes. See the inner exception for details.	Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.\n ---> Npgsql.PostgresException (0x80004005): 42703: column "IsActive" of relation "Conversations" does not exist\n\nPOSITION: 43\n   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n  Exception data:\n    Severity: ERROR\n    SqlState: 42703\n    MessageText: column "IsActive" of relation "Conversations" does not exist\n    Position: 43\n    File: parse_target.c\n    Line: 1075\n    Routine: checkInsertTargets\n   --- End of inner exception stack trace ---\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 357\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Backend.Middlewares.PresenceTrackingMiddleware.InvokeAsync(HttpContext context, ApplicationDbContext db) in /home/ubuntu/winplus/dotnet/Middlewares/PresenceTrackingMiddleware.cs:line 37\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 357\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Backend.Middlewares.PresenceTrackingMiddleware.InvokeAsync(HttpContext context, ApplicationDbContext db) in /home/ubuntu/winplus/dotnet/Middlewares/PresenceTrackingMiddleware.cs:line 37\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 2	/api/chatbot/stream	\N	2026-08-25 15:47:26.361275+00	f	\N
12	Error	DbUpdateException	An error occurred while saving the entity changes. See the inner exception for details.	Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.\n ---> Npgsql.PostgresException (0x80004005): 42703: column "IsActive" of relation "Conversations" does not exist\n\nPOSITION: 43\n   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n  Exception data:\n    Severity: ERROR\n    SqlState: 42703\n    MessageText: column "IsActive" of relation "Conversations" does not exist\n    Position: 43\n    File: parse_target.c\n    Line: 1075\n    Routine: checkInsertTargets\n   --- End of inner exception stack trace ---\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 357\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Backend.Middlewares.PresenceTrackingMiddleware.InvokeAsync(HttpContext context, ApplicationDbContext db) in /home/ubuntu/winplus/dotnet/Middlewares/PresenceTrackingMiddleware.cs:line 37\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 357\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Backend.Middlewares.PresenceTrackingMiddleware.InvokeAsync(HttpContext context, ApplicationDbContext db) in /home/ubuntu/winplus/dotnet/Middlewares/PresenceTrackingMiddleware.cs:line 37\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 2	/api/chatbot/stream	\N	2026-08-28 08:16:09.592358+00	f	\N
13	Error	PostgresException	42P01: relation "QuizAttempts" does not exist\n\nPOSITION: 210	Npgsql.PostgresException (0x80004005): 42P01: relation "QuizAttempts" does not exist\n\nPOSITION: 210\n   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SingleQueryingEnumerable`1.AsyncEnumerator.InitializeReaderAsync(AsyncEnumerator enumerator, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SingleQueryingEnumerable`1.AsyncEnumerator.MoveNextAsync()\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Backend.Services.QuizService.GetUserAllQuizAttemptsAsync(Int32 userId, Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Services/QuizService.cs:line 251\n   at Backend.Controllers.QuizzesController.GetMyQuizAttempts(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Controllers/QuizzesController.cs:line 183\n   at lambda_method1772(Closure, Object)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.AwaitableObjectResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Backend.Middlewares.PresenceTrackingMiddleware.InvokeAsync(HttpContext context, ApplicationDbContext db) in /home/ubuntu/winplus/dotnet/Middlewares/PresenceTrackingMiddleware.cs:line 37\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29\n  Exception data:\n    Severity: ERROR\n    SqlState: 42P01\n    MessageText: relation "QuizAttempts" does not exist\n    Position: 210\n    File: parse_relation.c\n    Line: 1381\n    Routine: parserOpenTable	   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SingleQueryingEnumerable`1.AsyncEnumerator.InitializeReaderAsync(AsyncEnumerator enumerator, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SingleQueryingEnumerable`1.AsyncEnumerator.MoveNextAsync()\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Backend.Services.QuizService.GetUserAllQuizAttemptsAsync(Int32 userId, Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Services/QuizService.cs:line 251\n   at Backend.Controllers.QuizzesController.GetMyQuizAttempts(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Controllers/QuizzesController.cs:line 183\n   at lambda_method1772(Closure, Object)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.AwaitableObjectResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoke	/api/quizzes/me/attempts	\N	2026-08-28 22:05:29.424791+00	f	\N
14	Error	PostgresException	42703: column r.Completions does not exist\n\nPOSITION: 16	Npgsql.PostgresException (0x80004005): 42703: column r.Completions does not exist\n\nPOSITION: 16\n   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SingleQueryingEnumerable`1.AsyncEnumerator.InitializeReaderAsync(AsyncEnumerator enumerator, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SingleQueryingEnumerable`1.AsyncEnumerator.MoveNextAsync()\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Backend.Services.RevisionService.GetAssignedRevisionsAsync(Int32 userId, Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Services/RevisionService.cs:line 100\n   at Backend.Controllers.RevisionsController.GetMyAssignedRevisions(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Controllers/RevisionsController.cs:line 87\n   at lambda_method1899(Closure, Object)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.AwaitableObjectResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Backend.Middlewares.PresenceTrackingMiddleware.InvokeAsync(HttpContext context, ApplicationDbContext db) in /home/ubuntu/winplus/dotnet/Middlewares/PresenceTrackingMiddleware.cs:line 37\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29\n  Exception data:\n    Severity: ERROR\n    SqlState: 42703\n    MessageText: column r.Completions does not exist\n    Position: 16\n    File: parse_relation.c\n    Line: 3647\n    Routine: errorMissingColumn	   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SingleQueryingEnumerable`1.AsyncEnumerator.InitializeReaderAsync(AsyncEnumerator enumerator, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SingleQueryingEnumerable`1.AsyncEnumerator.MoveNextAsync()\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Backend.Services.RevisionService.GetAssignedRevisionsAsync(Int32 userId, Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Services/RevisionService.cs:line 100\n   at Backend.Controllers.RevisionsController.GetMyAssignedRevisions(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Controllers/RevisionsController.cs:line 87\n   at lambda_method1899(Closure, Object)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.AwaitableObjectResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(	/api/revisions/me/assigned	\N	2026-08-28 22:05:57.251593+00	f	\N
15	Error	DbUpdateException	An error occurred while saving the entity changes. See the inner exception for details.	Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.\n ---> Npgsql.PostgresException (0x80004005): 42703: column "IsActive" of relation "Conversations" does not exist\n\nPOSITION: 43\n   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n  Exception data:\n    Severity: ERROR\n    SqlState: 42703\n    MessageText: column "IsActive" of relation "Conversations" does not exist\n    Position: 43\n    File: parse_target.c\n    Line: 1075\n    Routine: checkInsertTargets\n   --- End of inner exception stack trace ---\n   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 369\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Backend.Middlewares.PresenceTrackingMiddleware.InvokeAsync(HttpContext context, ApplicationDbContext db) in /home/ubuntu/winplus/dotnet/Middlewares/PresenceTrackingMiddleware.cs:line 37\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)\n   at Backend.Controllers.ChatbotController.StreamChat(StreamChatRequest request, CancellationToken cancellationToken) in /home/ubuntu/winplus/dotnet/Controllers/ChatbotController.cs:line 369\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Backend.Middlewares.PresenceTrackingMiddleware.InvokeAsync(HttpContext context, ApplicationDbContext db) in /home/ubuntu/winplus/dotnet/Middlewares/PresenceTrackingMiddleware.cs:line 37\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 2	/api/chatbot/stream	\N	2026-08-29 15:58:27.775622+00	f	\N
17	Error	PostgresException	42703: column q.Subject does not exist\n\nPOSITION: 253	Npgsql.PostgresException (0x80004005): 42703: column q.Subject does not exist\n\nPOSITION: 253\n   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SplitQueryingEnumerable`1.AsyncEnumerator.InitializeReaderAsync(AsyncEnumerator enumerator, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SplitQueryingEnumerable`1.AsyncEnumerator.MoveNextAsync()\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Backend.Services.QuizService.GetPublishedQuizzesAsync(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Services/QuizService.cs:line 131\n   at Backend.Controllers.QuizzesController.GetPublishedQuizzes(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Controllers/QuizzesController.cs:line 110\n   at lambda_method1247(Closure, Object)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.AwaitableObjectResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Backend.Middlewares.PresenceTrackingMiddleware.InvokeAsync(HttpContext context, ApplicationDbContext db) in /home/ubuntu/winplus/dotnet/Middlewares/PresenceTrackingMiddleware.cs:line 37\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29\n  Exception data:\n    Severity: ERROR\n    SqlState: 42703\n    MessageText: column q.Subject does not exist\n    Hint: Perhaps you meant to reference the column "q.SubjectId".\n    Position: 253\n    File: parse_relation.c\n    Line: 3647\n    Routine: errorMissingColumn	   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong(Boolean async, DataRowLoadingMode dataRowLoadingMode, Boolean readingNotifications, Boolean isReadingPrependedMessage)\n   at System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1.StateMachineBox`1.System.Threading.Tasks.Sources.IValueTaskSource<TResult>.GetResult(Int16 token)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlDataReader.NextResult(Boolean async, Boolean isConsuming, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteReader(Boolean async, CommandBehavior behavior, CancellationToken cancellationToken)\n   at Npgsql.NpgsqlCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SplitQueryingEnumerable`1.AsyncEnumerator.InitializeReaderAsync(AsyncEnumerator enumerator, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SplitQueryingEnumerable`1.AsyncEnumerator.MoveNextAsync()\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Backend.Services.QuizService.GetPublishedQuizzesAsync(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Services/QuizService.cs:line 131\n   at Backend.Controllers.QuizzesController.GetPublishedQuizzes(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Controllers/QuizzesController.cs:line 110\n   at lambda_method1247(Closure, Object)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.AwaitableObjectResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task t	/api/quizzes/published	\N	2026-09-01 07:51:04.712117+00	f	\N
16	Error	InvalidCastException	Reading as 'System.Int32' is not supported for fields having DataTypeName 'character varying'	System.InvalidCastException: Reading as 'System.Int32' is not supported for fields having DataTypeName 'character varying'\n   at Npgsql.Internal.AdoSerializerHelpers.<GetTypeInfoForReading>g__ThrowReadingNotSupported|0_0(Type type, String displayName, Exception inner)\n   at Npgsql.Internal.AdoSerializerHelpers.GetTypeInfoForReading(Type type, PostgresType postgresType, PgSerializerOptions options)\n   at Npgsql.BackendMessages.FieldDescription.<GetInfo>g__GetInfoSlow|50_0(Type type, ColumnInfo& lastColumnInfo)\n   at Npgsql.BackendMessages.FieldDescription.GetInfo(Type type, ColumnInfo& lastColumnInfo)\n   at Npgsql.NpgsqlDataReader.<GetInfo>g__Slow|133_0(ColumnInfo& info, PgConverter& converter, Size& bufferRequirement, Boolean& asObject, <>c__DisplayClass133_0&)\n   at Npgsql.NpgsqlDataReader.GetFieldValueCore[T](Int32 ordinal)\n   at Npgsql.NpgsqlDataReader.GetInt32(Int32 ordinal)\n   at Microsoft.EntityFrameworkCore.Query.Internal.BufferedDataReader.BufferedDataRecord.ReadInt(DbDataReader reader, Int32 ordinal, ReaderColumn column)\n   at Microsoft.EntityFrameworkCore.Query.Internal.BufferedDataReader.BufferedDataRecord.ReadRow()\n   at Microsoft.EntityFrameworkCore.Query.Internal.BufferedDataReader.BufferedDataRecord.InitializeAsync(DbDataReader reader, IReadOnlyList`1 columns, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.BufferedDataReader.InitializeAsync(IReadOnlyList`1 columns, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.BufferedDataReader.InitializeAsync(IReadOnlyList`1 columns, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SplitQueryingEnumerable`1.AsyncEnumerator.InitializeReaderAsync(AsyncEnumerator enumerator, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SplitQueryingEnumerable`1.AsyncEnumerator.MoveNextAsync()\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Backend.Services.RevisionService.GetPublishedRevisionsAsync(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Services/RevisionService.cs:line 130\n   at Backend.Controllers.RevisionsController.GetPublishedRevisions(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Controllers/RevisionsController.cs:line 111\n   at lambda_method1232(Closure, Object)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.AwaitableObjectResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeFilterPipelineAsync>g__Awaited|20_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeAsync>g__Awaited|17_0(ResourceInvoker invoker, Task task, IDisposable scope)\n   at Backend.Middlewares.PresenceTrackingMiddleware.InvokeAsync(HttpContext context, ApplicationDbContext db) in /home/ubuntu/winplus/dotnet/Middlewares/PresenceTrackingMiddleware.cs:line 37\n   at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)\n   at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)\n   at Backend.Middlewares.RateLimitingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/RateLimitingMiddleware.cs:line 87\n   at Backend.Middlewares.ErrorHandlingMiddleware.InvokeAsync(HttpContext context) in /home/ubuntu/winplus/dotnet/Middlewares/ErrorHandlingMiddleware.cs:line 29	   at Npgsql.Internal.AdoSerializerHelpers.<GetTypeInfoForReading>g__ThrowReadingNotSupported|0_0(Type type, String displayName, Exception inner)\n   at Npgsql.Internal.AdoSerializerHelpers.GetTypeInfoForReading(Type type, PostgresType postgresType, PgSerializerOptions options)\n   at Npgsql.BackendMessages.FieldDescription.<GetInfo>g__GetInfoSlow|50_0(Type type, ColumnInfo& lastColumnInfo)\n   at Npgsql.BackendMessages.FieldDescription.GetInfo(Type type, ColumnInfo& lastColumnInfo)\n   at Npgsql.NpgsqlDataReader.<GetInfo>g__Slow|133_0(ColumnInfo& info, PgConverter& converter, Size& bufferRequirement, Boolean& asObject, <>c__DisplayClass133_0&)\n   at Npgsql.NpgsqlDataReader.GetFieldValueCore[T](Int32 ordinal)\n   at Npgsql.NpgsqlDataReader.GetInt32(Int32 ordinal)\n   at Microsoft.EntityFrameworkCore.Query.Internal.BufferedDataReader.BufferedDataRecord.ReadInt(DbDataReader reader, Int32 ordinal, ReaderColumn column)\n   at Microsoft.EntityFrameworkCore.Query.Internal.BufferedDataReader.BufferedDataRecord.ReadRow()\n   at Microsoft.EntityFrameworkCore.Query.Internal.BufferedDataReader.BufferedDataRecord.InitializeAsync(DbDataReader reader, IReadOnlyList`1 columns, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.BufferedDataReader.InitializeAsync(IReadOnlyList`1 columns, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.BufferedDataReader.InitializeAsync(IReadOnlyList`1 columns, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SplitQueryingEnumerable`1.AsyncEnumerator.InitializeReaderAsync(AsyncEnumerator enumerator, CancellationToken cancellationToken)\n   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Query.Internal.SplitQueryingEnumerable`1.AsyncEnumerator.MoveNextAsync()\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync[TSource](IQueryable`1 source, CancellationToken cancellationToken)\n   at Backend.Services.RevisionService.GetPublishedRevisionsAsync(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Services/RevisionService.cs:line 130\n   at Backend.Controllers.RevisionsController.GetPublishedRevisions(Int32 page, Int32 pageSize) in /home/ubuntu/winplus/dotnet/Controllers/RevisionsController.cs:line 111\n   at lambda_method1232(Closure, Object)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.AwaitableObjectResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Awaited|12_0(ControllerActionInvoker invoker, ValueTask`1 actionResultValueTask)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g_	/api/revisions/published	\N	2026-09-01 07:51:04.682245+00	f	\N
\.


--
-- Data for Name: BackupCodes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."BackupCodes" ("Id", "TwoFactorTokenId", "Code", "IsUsed", "UsedAt", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: CartItems; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."CartItems" ("Id", "UserId", "SubjectId", "Price", "AddedAt") FROM stdin;
3	2	19	44.99	2026-02-15 13:09:19.142134+00
4	5	8	27.99	2026-02-17 13:09:19.142134+00
5	9	1	29.99	2026-02-14 13:09:19.142134+00
\.


--
-- Data for Name: Certificates; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Certificates" ("Id", "UserId", "EnrollmentId", "SubjectId", "Title", "CertificateUrl", "CertificateNumber", "FinalScore", "IssuedAt", "CreatedAt", "CompletionDate", "VerificationCode") FROM stdin;
1	2	4	1	Certificat - Mathématiques Terminale S	https://certs.winplus.cm/cert/WPC-2026-0001.pdf	WPC-2026-0001	92.50	2026-02-15 16:00:00+00	2026-02-18 14:55:59.537933+00	2026-02-15 16:00:00	\N
2	2	5	3	Certificat - Français Dissertation	https://certs.winplus.cm/cert/WPC-2026-0002.pdf	WPC-2026-0002	88.00	2026-02-10 14:00:00+00	2026-02-18 14:55:59.537933+00	2026-02-10 14:00:00	\N
3	4	9	5	Certificat - Anglais Conversationnel	https://certs.winplus.cm/cert/WPC-2026-0003.pdf	WPC-2026-0003	85.50	2026-02-12 11:00:00+00	2026-02-18 14:55:59.537933+00	2026-02-12 11:00:00	\N
\.


--
-- Data for Name: ChatbotContexts; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ChatbotContexts" ("Id", "UserId", "EducationLevel", "Grade", "UserObjectives", "EnrolledSubjects", "RecentActivity", "NavigationHistory", "Preferences", "Strengths", "Weaknesses", "LearningStyle", "CreatedAt", "UpdatedAt", "PerformanceHistory", "ForceLanguage") FROM stdin;
1	37	\N	\N	\N	\N	\N	\N	\N	\N	\N	\N	2026-08-28 21:51:13.082281+00	2026-08-28 21:51:13.082318+00	\N	\N
2	36	\N	\N	\N	\N	\N	\N	\N	\N	\N	\N	2026-08-29 09:43:19.224435+00	2026-08-29 09:43:19.224487+00	\N	\N
3	17	\N	\N	\N	\N	\N	\N	\N	\N	\N	\N	2026-08-29 19:41:49.203915+00	2026-08-29 19:41:49.203953+00	\N	\N
4	38	\N	\N	\N	\N	\N	\N	\N	\N	\N	\N	2026-08-29 20:19:57.458533+00	2026-08-29 20:19:57.458577+00	\N	\N
5	30	\N	\N	\N	\N	\N	\N	\N	\N	\N	\N	2026-08-31 03:16:28.990789+00	2026-08-31 03:16:28.990826+00	\N	\N
6	39	\N	\N	\N	\N	\N	\N	\N	\N	\N	\N	2026-09-01 09:09:07.534697+00	2026-09-01 09:09:07.534995+00	\N	\N
\.


--
-- Data for Name: ConcoursEvents; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ConcoursEvents" ("Id", "Slug", "Name", "Year", "RegistrationStartDate", "RegistrationEndDate", "ExamDate", "ResultsDate", "Location", "EnrollmentFeeXaf", "OfficialRegistrationUrl", "Notes", "IsPublished", "CreatedAt", "UpdatedAt") FROM stdin;
1	ens	ENS Yaoundé	2026	2026-01-15 00:00:00+00	2026-03-31 23:59:59+00	2026-06-15 07:00:00+00	2026-09-01 00:00:00+00	Yaoundé	15000	https://minesup.gov.cm	\N	t	2026-09-01 20:19:31.00156+00	2026-09-01 20:19:31.00156+00
2	polytechnique	École Polytechnique de Yaoundé	2026	2026-02-01 00:00:00+00	2026-04-15 23:59:59+00	2026-07-01 07:00:00+00	2026-09-15 00:00:00+00	Yaoundé	20000	https://polytechnique.cm	\N	t	2026-09-01 20:19:31.00156+00	2026-09-01 20:19:31.00156+00
3	enam	ENAM	2026	2026-01-20 00:00:00+00	2026-04-01 23:59:59+00	2026-06-20 07:00:00+00	2026-10-01 00:00:00+00	Yaoundé / Douala	15000	https://enam.cm	\N	t	2026-09-01 20:19:31.00156+00	2026-09-01 20:19:31.00156+00
4	fmsb	Faculté de Médecine et Sciences Biomédicales	2026	2026-03-01 00:00:00+00	2026-05-31 23:59:59+00	2026-08-10 07:00:00+00	2026-11-01 00:00:00+00	Yaoundé	25000	https://fmsb.cm	\N	t	2026-09-01 20:19:31.00156+00	2026-09-01 20:19:31.00156+00
5	essec	ESSEC Douala	2026	2026-02-15 00:00:00+00	2026-04-30 23:59:59+00	2026-07-15 07:00:00+00	2026-10-15 00:00:00+00	Douala	15000	https://essec.cm	\N	t	2026-09-01 20:19:31.00156+00	2026-09-01 20:19:31.00156+00
6	enset	ENSET	2026	2026-01-25 00:00:00+00	2026-03-31 23:59:59+00	2026-06-25 07:00:00+00	2026-09-20 00:00:00+00	Yaoundé / Douala	12000	https://enset.cm	\N	t	2026-09-01 20:19:31.00156+00	2026-09-01 20:19:31.00156+00
7	bac	Baccalauréat Camerounais	2026	\N	\N	2026-06-01 07:00:00+00	2026-08-20 00:00:00+00	National	\N	https://obc.cm	Toutes séries	t	2026-09-01 20:19:31.00156+00	2026-09-01 20:19:31.00156+00
8	bepc	Brevet d'Études du Premier Cycle	2026	\N	\N	2026-05-20 07:00:00+00	2026-07-20 00:00:00+00	National	\N	https://obc.cm	\N	t	2026-09-01 20:19:31.00156+00	2026-09-01 20:19:31.00156+00
9	probatoire	Probatoire	2026	\N	\N	2026-05-27 07:00:00+00	2026-07-25 00:00:00+00	National	\N	https://obc.cm	\N	t	2026-09-01 20:19:31.00156+00	2026-09-01 20:19:31.00156+00
\.


--
-- Data for Name: Conversations; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Conversations" ("Id", "UserId", "Title", "CreatedAt", "UpdatedAt", "IsDeleted", "IsActive", "Tags", "Metadata", "LastMessageAt", "MessageCount") FROM stdin;
1	37	Bonjour	2026-08-30 22:12:32.685508+00	2026-08-30 22:12:45.884978+00	f	t	\N	\N	2026-08-30 22:12:45.884976	0
2	37	Bonjour	2026-08-30 22:35:27.495428+00	2026-08-31 01:56:21.376591+00	f	t	\N	\N	2026-08-31 01:56:21.376526	0
3	37	Bonjour	2026-08-31 01:58:24.519096+00	2026-08-31 01:58:24.539+00	f	t	\N	\N	2026-08-31 01:58:24.538999	0
4	37	Bonjour	2026-08-31 02:44:59.982758+00	2026-08-31 02:45:00.195335+00	f	t	\N	\N	2026-08-31 02:45:00.195286	0
5	30	Résume ma progression	2026-08-31 03:16:46.025412+00	2026-08-31 03:27:07.015778+00	f	t	\N	\N	2026-08-31 03:27:07.015777	0
6	30	Résume ma progression	2026-08-31 03:31:32.961396+00	2026-08-31 03:33:25.368895+00	f	t	\N	\N	2026-08-31 03:33:25.368894	0
7	30	Quiz surprise sur mes lacunes	2026-08-31 03:37:10.442773+00	2026-08-31 03:39:05.169012+00	f	t	\N	\N	2026-08-31 03:39:05.169011	0
8	30	Salut	2026-08-31 03:42:19.977377+00	2026-08-31 03:42:21.468269+00	f	t	\N	\N	2026-08-31 03:42:21.468269	0
9	30	Tu explores le catalogue  je peux te recommander l…	2026-08-31 03:47:01.761342+00	2026-08-31 03:47:04.119548+00	f	t	\N	\N	2026-08-31 03:47:04.119548	0
10	30	Bonjour	2026-08-31 16:10:21.348853+00	2026-08-31 16:11:06.590265+00	f	t	\N	\N	2026-08-31 16:11:06.590264	0
11	39	Bonne journée paolo ! Ta session du jour : révisio…	2026-09-01 08:58:49.637852+00	2026-09-01 09:00:36.798103+00	f	t	\N	\N	2026-09-01 09:00:36.798102	0
12	39	Tu explores le catalogue  je peux te recommander l…	2026-09-01 09:04:49.744577+00	2026-09-01 09:04:52.78954+00	f	t	\N	\N	2026-09-01 09:04:52.78954	0
\.


--
-- Data for Name: CourseContents; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."CourseContents" ("Id", "SubjectId", "Title", "Description", "VideoUrl", "DocumentUrl", "OrderIndex", "DurationMinutes", "IsLocked", "CreatedAt", "UpdatedAt", "CreatedByUserId", "Status") FROM stdin;
3	1	Introduction aux Limites	Comprendre le concept de limite d'une fonction.	https://videos.winplus.cm/maths/limites-intro.mp4	\N	1	45	f	2026-01-05 10:00:00+00	\N	\N	published
4	1	Calcul de Limites	Méthodes et techniques de calcul de limites.	https://videos.winplus.cm/maths/limites-calcul.mp4	\N	2	50	f	2026-01-05 10:00:00+00	\N	\N	published
5	1	Dérivées - Fondamentaux	Définition et règles de dérivation.	https://videos.winplus.cm/maths/derivees-bases.mp4	\N	3	55	f	2026-01-06 10:00:00+00	\N	\N	published
6	1	Applications des Dérivées	Étude de fonctions, tangentes et optimisation.	https://videos.winplus.cm/maths/derivees-applications.mp4	\N	4	60	t	2026-01-07 10:00:00+00	\N	\N	published
7	1	Intégrales - Introduction	Concept d'intégrale et primitives.	https://videos.winplus.cm/maths/integrales-intro.mp4	\N	5	50	t	2026-01-08 10:00:00+00	\N	\N	published
8	2	Les Lois de Newton	Les 3 lois fondamentales de la mécanique.	https://videos.winplus.cm/physique/newton.mp4	\N	1	40	f	2026-01-05 10:00:00+00	\N	\N	published
9	2	Énergie Cinétique et Potentielle	Conservation et transformation de l'énergie.	https://videos.winplus.cm/physique/energie.mp4	\N	2	45	f	2026-01-06 10:00:00+00	\N	\N	published
10	2	Réactions Chimiques	Équilibrer les équations et stœchiométrie.	https://videos.winplus.cm/physique/reactions.mp4	\N	3	50	t	2026-01-07 10:00:00+00	\N	\N	published
11	3	Méthodologie de la Dissertation	Les étapes clés pour rédiger une dissertation réussie.	https://videos.winplus.cm/francais/methodo-dissert.mp4	\N	1	40	f	2026-01-05 10:00:00+00	\N	\N	published
12	3	Construire un Plan	Thèse, antithèse, synthèse et plan dialectique.	https://videos.winplus.cm/francais/plan.mp4	\N	2	35	f	2026-01-06 10:00:00+00	\N	\N	published
13	3	L'Introduction Parfaite	Accrocher le lecteur et poser la problématique.	https://videos.winplus.cm/francais/intro.mp4	\N	3	30	f	2026-01-07 10:00:00+00	\N	\N	published
14	3	Exemples et Arguments	Illustrer avec des références littéraires pertinentes.	https://videos.winplus.cm/francais/exemples.mp4	\N	4	45	t	2026-01-08 10:00:00+00	\N	\N	published
15	5	Greetings & Introductions	Learn how to introduce yourself naturally.	https://videos.winplus.cm/anglais/greetings.mp4	\N	1	30	f	2026-01-05 10:00:00+00	\N	\N	published
16	5	Everyday Conversations	Practice common daily dialogues and expressions.	https://videos.winplus.cm/anglais/everyday.mp4	\N	2	35	f	2026-01-06 10:00:00+00	\N	\N	published
17	5	Pronunciation Workshop	Master tricky English sounds and intonation.	https://videos.winplus.cm/anglais/pronunciation.mp4	\N	3	40	f	2026-01-07 10:00:00+00	\N	\N	published
18	9	Introduction à Python	Installation, variables et premiers programmes.	https://videos.winplus.cm/python/intro.mp4	\N	1	45	f	2026-01-05 10:00:00+00	\N	\N	published
19	9	Structures de Contrôle	Conditions if/else, boucles for et while.	https://videos.winplus.cm/python/controle.mp4	\N	2	50	f	2026-01-06 10:00:00+00	\N	\N	published
20	9	Fonctions et Modules	Créer des fonctions réutilisables et importer des modules.	https://videos.winplus.cm/python/fonctions.mp4	\N	3	45	f	2026-01-07 10:00:00+00	\N	\N	published
21	9	Listes et Dictionnaires	Manipuler les structures de données essentielles.	https://videos.winplus.cm/python/listes.mp4	\N	4	50	f	2026-01-08 10:00:00+00	\N	\N	published
22	9	Projet Final - Jeu en Python	Créer un jeu complet en utilisant toutes les notions apprises.	https://videos.winplus.cm/python/projet-jeu.mp4	\N	5	60	t	2026-01-09 10:00:00+00	\N	\N	published
23	26	React Fondamentaux	JSX, composants, props et state.	https://videos.winplus.cm/react/fondamentaux.mp4	\N	1	55	f	2026-01-15 10:00:00+00	\N	\N	published
24	26	Hooks Essentiels	useState, useEffect et cycle de vie.	https://videos.winplus.cm/react/hooks.mp4	\N	2	50	f	2026-01-16 10:00:00+00	\N	\N	published
25	26	Routing avec Next.js	Pages, navigation et layouts avec Next.js.	https://videos.winplus.cm/react/routing.mp4	\N	3	45	t	2026-01-17 10:00:00+00	\N	\N	published
26	26	API Routes & Data Fetching	getServerSideProps, getStaticProps et API routes.	https://videos.winplus.cm/react/api-routes.mp4	\N	4	55	t	2026-01-18 10:00:00+00	\N	\N	published
27	20	Introduction au Marketing Digital	Les fondamentaux du marketing en ligne.	https://videos.winplus.cm/marketing/intro.mp4	\N	1	40	f	2026-01-10 10:00:00+00	\N	\N	published
28	20	Réseaux Sociaux & Stratégie	Créer une présence efficace sur les réseaux sociaux.	https://videos.winplus.cm/marketing/reseaux.mp4	\N	2	45	f	2026-01-11 10:00:00+00	\N	\N	published
29	20	SEO & Référencement	Optimiser son site pour les moteurs de recherche.	https://videos.winplus.cm/marketing/seo.mp4	\N	3	50	t	2026-01-12 10:00:00+00	\N	\N	published
30	8	La Cellule Humaine	Structure et fonctions de la cellule.	https://videos.winplus.cm/bio/cellule.mp4	\N	1	45	f	2026-01-05 10:00:00+00	\N	\N	published
31	8	Le Système Digestif	Anatomie et physiologie de la digestion.	https://videos.winplus.cm/bio/digestif.mp4	\N	2	40	f	2026-01-06 10:00:00+00	\N	\N	published
32	8	Le Système Nerveux	Neurones, synapses et transmission nerveuse.	https://videos.winplus.cm/bio/nerveux.mp4	\N	3	50	f	2026-01-07 10:00:00+00	\N	\N	published
33	31	Règlementation COBAC	CONVENTION PORTANT HARMONISATION DE LA\nREGLEMENTATION BANCAIRE DANS LES ETATS DE\nL’AFRIQUE CENTRALE	\N	https://winplus-files-prod.s3.eu-north-1.amazonaws.com/library/2026/09/7187a28920a549ffa98267717b19607f.pdf	0	0	f	2026-09-01 22:05:23.689606+00	\N	\N	draft
\.


--
-- Data for Name: CourseEnrollments; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."CourseEnrollments" ("Id", "UserId", "CourseId", "AccessType", "IsActive", "ProgressPercent", "EnrolledAt", "LastAccessedAt", "CompletedAt", "CertificateUrl", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: CourseLessons; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."CourseLessons" ("Id", "SectionId", "CourseId", "Title", "LessonType", "Description", "VideoUrl", "VideoDurationSec", "ArticleContent", "FileUrl", "FileName", "Position", "IsPreview", "IsPublished", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: CourseReviews; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."CourseReviews" ("Id", "CourseId", "UserId", "Rating", "Comment", "IsVerified", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: CourseSections; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."CourseSections" ("Id", "CourseId", "Title", "Description", "Position", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: Courses; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Courses" ("Id", "InstructorId", "Title", "Slug", "Description", "ShortDescription", "ThumbnailUrl", "PreviewVideoUrl", "Language", "Level", "Category", "Tags", "Price", "IsFree", "IsIncludedInSub", "Status", "Requirements", "Objectives", "TotalDurationMin", "LessonsCount", "EnrolledCount", "AvgRating", "ReviewsCount", "CertificateEnabled", "PublishedAt", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: DeviceInfos; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."DeviceInfos" ("Id", "UserId", "DeviceFingerprint", "UserAgent", "IpAddress", "BrowserName", "BrowserVersion", "OSName", "OSVersion", "DeviceName", "RememberUntil", "LastUsedAt", "CreatedAt") FROM stdin;
2	17	54cTFmi7xTLyPrca7w6JcAr35gb358DA5P80S5m7u/w=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36	129.0.60.46	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-14 19:38:31.76153+00	2026-06-14 18:43:27.953055+00
3	21	4dVD9ZIafuCrFVBZAI815CUAKcy84uyyPF3VkyduX54=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36	129.0.60.50	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-15 01:24:57.566806+00	2026-06-14 21:37:59.844658+00
4	21	hiC3qnbvQrxolleAp2xD0DAO0+c9jUA+eFv+f76Iv7M=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36	129.0.60.52	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-15 06:33:58.675668+00	2026-06-15 06:33:58.674857+00
5	17	D9YMUUwxVhSGR0Annr9x4E1TziunG+UlRe6KWYpfT4w=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36	129.0.60.54	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-16 15:41:55.766692+00	2026-06-16 15:41:55.765784+00
6	21	D9YMUUwxVhSGR0Annr9x4E1TziunG+UlRe6KWYpfT4w=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36	129.0.60.54	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-16 15:49:05.617216+00	2026-06-16 15:49:05.617191+00
7	20	D9YMUUwxVhSGR0Annr9x4E1TziunG+UlRe6KWYpfT4w=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36	129.0.60.54	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-16 15:50:10.595962+00	2026-06-16 15:50:10.595949+00
8	21	p+rLf0EEe+prcB7vF6Q98aQ72CFejVwI+ZtZMrdsyHo=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36	129.0.60.47	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-17 10:12:48.741048+00	2026-06-17 10:12:48.740247+00
9	18	16gHp4vjp7V5f1cbSn+yhdy6sYW2wzJjx0Ic7dD1O1M=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36	129.0.60.58	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-17 10:14:10.674014+00	2026-06-17 10:14:10.674003+00
10	20	PvSXwgD5NXO5ss26zYKV9H8D8EcH11U9fgclh/4ltZc=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Mobile Safari/537.36	129.0.60.179	Chrome	149	Android	Unknown	Android Device	\N	2026-06-17 15:25:51.406252+00	2026-06-17 15:25:51.406241+00
11	21	PvSXwgD5NXO5ss26zYKV9H8D8EcH11U9fgclh/4ltZc=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Mobile Safari/537.36	129.0.60.179	Chrome	149	Android	Unknown	Android Device	\N	2026-06-17 15:28:01.547169+00	2026-06-17 15:28:01.547158+00
12	17	PvSXwgD5NXO5ss26zYKV9H8D8EcH11U9fgclh/4ltZc=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Mobile Safari/537.36	129.0.60.179	Chrome	149	Android	Unknown	Android Device	\N	2026-06-17 15:32:47.445094+00	2026-06-17 15:32:47.445072+00
24	17	+XEeMjkZ5RmI9U/02r+J8MSxjONi578VZeeP8mOe+lM=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0	154.72.160.103	Chrome	151	Windows	10.0	Windows PC	\N	2026-08-04 07:29:31.764104+00	2026-08-04 07:29:31.76338+00
13	21	TCE6zIWe6BGaPOIhJDTbfXmgQsOxxSzJWOVwnP336UY=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36 Edg/149.0.0.0	129.0.60.50	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-22 19:58:20.225168+00	2026-06-22 13:14:02.508437+00
16	21	aZkfyM9eYTIDBYJd/yjGu7w8gHpidjTxgexN5EDPw84=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36 Edg/149.0.0.0	129.0.60.57	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-23 10:20:02.234803+00	2026-06-23 10:20:02.233966+00
17	20	UG8ilLBeR2htVhNlrKEWyjNK2AZeYRBNUusaSdnIINM=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36 Edg/149.0.0.0	129.0.60.48	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-23 14:47:23.747915+00	2026-06-23 14:47:23.747891+00
19	21	UG8ilLBeR2htVhNlrKEWyjNK2AZeYRBNUusaSdnIINM=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36 Edg/149.0.0.0	129.0.60.48	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-23 17:19:30.238515+00	2026-06-23 17:19:30.237689+00
14	20	ZcIu6tqrA0wxeA5P0PuLSG5LQk42hLn8RtoNu7VSYiU=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Mobile Safari/537.36	129.0.60.177	Chrome	149	Android	Unknown	Android Device	\N	2026-06-22 16:00:02.515802+00	2026-06-22 16:00:02.515791+00
15	18	ZcIu6tqrA0wxeA5P0PuLSG5LQk42hLn8RtoNu7VSYiU=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Mobile Safari/537.36	129.0.60.177	Chrome	149	Android	Unknown	Android Device	\N	2026-06-22 16:05:22.92353+00	2026-06-22 16:05:22.923516+00
18	17	UG8ilLBeR2htVhNlrKEWyjNK2AZeYRBNUusaSdnIINM=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36 Edg/149.0.0.0	129.0.60.48	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-23 17:39:12.672723+00	2026-06-23 14:50:09.842357+00
20	21	cY0z8yl78ZRT2lYbhOt4qEYLzWTcTJIGXun1nKi7j1s=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36	129.0.60.48	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-23 17:40:46.135661+00	2026-06-23 17:40:46.135646+00
25	17	2ndrsRTnAX9qN8Uwdf4PdpQKx18pYNwDv+xD0QpNB9w=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	154.72.163.195	Chrome	151	Windows	10.0	Windows PC	\N	2026-08-22 12:46:56.029602+00	2026-08-22 12:46:41.956329+00
21	17	sK+7mhu4y+rnfA4yLBTLt5SqXd5qDcROt9nOcUe5chk=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36	129.0.60.53	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-24 09:18:43.922459+00	2026-06-24 09:18:43.921656+00
22	17	kaYSKvEAIyKhtscRrdq2S99OOhttIK9amZZTC7n7OuI=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36 Edg/149.0.0.0	129.0.60.53	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-24 11:05:38.13542+00	2026-06-24 11:05:38.134641+00
23	21	QqJQmWZQCwwQaGv5DKYptdxa/JQ3+TFdO/nAI1Pzogg=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36 Edg/149.0.0.0	102.244.222.64	Chrome	149	Windows	10.0	Windows PC	\N	2026-06-29 14:28:14.403719+00	2026-06-29 14:28:14.402866+00
26	17	4l0gO+ZWHmXaQU4rZ2d+makYqQcgSTsDt8ZZTwlWV5Q=	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	154.72.163.195	Chrome	151	Android	Unknown	Android Device	\N	2026-08-22 12:47:23.825665+00	2026-08-22 12:47:23.825656+00
28	17	gI0WQBIsUWNbh+tcDoSx+U6wwdcfYROqsBNvHfE8zV0=	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	154.72.160.57	Chrome	151	Android	Unknown	Android Device	\N	2026-08-23 14:08:34.620288+00	2026-08-23 14:08:34.619633+00
27	17	u6hB/TN4uvbC865R69oTygr/AqH8r7vNMArkm6f6P2A=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	154.72.160.57	Chrome	151	Windows	10.0	Windows PC	\N	2026-08-23 13:57:04.790255+00	2026-08-23 13:50:23.420002+00
29	37	E9c3s/Q8GbkMpR10UY9gkccqUaEotZTdeY4sOyWRbzc=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	143.105.152.169	Chrome	151	Windows	10.0	Windows PC	\N	2026-08-27 10:00:55.468791+00	2026-08-27 10:00:55.467995+00
30	37	28xEmbO3PD3YPFgbYHz9ADMdDVHpJxypdtPCQ3y1VRU=	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	154.72.162.101	Chrome	151	Android	Unknown	Android Device	\N	2026-08-28 07:50:21.049107+00	2026-08-28 07:50:21.048158+00
31	37	twA7ys/LlNCS7k+n9/rAPhd/ONGJ6LHudfkCq9kuKCY=	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	154.72.162.101	Safari	26.6	iOS		iPhone	\N	2026-08-28 07:51:39.063544+00	2026-08-28 07:51:39.063531+00
32	36	3O1XjTnv7NTJHMXRdNjUTemAzJ7pB2YST9dOOFhA3Gk=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Mobile Safari/537.36	102.244.128.105	Chrome	150	Android	Unknown	Android Device	\N	2026-08-28 08:15:28.818369+00	2026-08-28 08:15:28.817387+00
33	37	rppGr6zb9c8O1Q3VIt8ExH1Ws+qaxGxQgTnKnEASxlE=	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	129.0.226.230	Safari	26.6	iOS		iPhone	\N	2026-08-28 15:14:35.688549+00	2026-08-28 15:14:35.687692+00
34	37	z4uxT3sI0bW1DavxXhIcH06PDppiKUa0UpLBdCODKq4=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	154.72.161.245	Chrome	151	Windows	10.0	Windows PC	\N	2026-08-28 22:03:07.564748+00	2026-08-28 22:03:07.563892+00
36	36	yb9u4khs8ZOXL9FbkRxuzz/qHtlJfiWyL8AfCvc/uZM=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	102.244.128.105	Chrome	151	Android	Unknown	Android Device	\N	2026-08-29 09:45:10.257372+00	2026-08-29 09:45:10.256674+00
53	37	XMmQfUW16AhQfUMEiaelGdxNK1uarS1wPc1MIrGDGQo=	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	154.72.161.183	Safari	26.6	iOS		iPhone	\N	2026-09-01 06:18:54.160333+00	2026-09-01 06:18:54.159366+00
35	37	/vkD87Oqg94cwuQn4mCkNJgNf1YeXNcaTws/uNjbYeQ=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	154.72.160.101	Chrome	151	Windows	10.0	Windows PC	\N	2026-08-29 12:50:54.25022+00	2026-08-29 08:10:03.685224+00
37	37	OoN4JyySBQ+eDZsINKT63f1/0e7iRsD4D8ntsCC4N2M=	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	154.72.160.101	Safari	26.6	iOS		iPhone	\N	2026-08-29 14:55:05.509294+00	2026-08-29 10:02:41.12976+00
38	37	nsb4HYQMnsU1n7LeeyjNOmmTYNrWa5sulQUbQlxfKE8=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	154.72.161.110	Chrome	151	Android	Unknown	Android Device	\N	2026-08-29 17:46:03.8443+00	2026-08-29 17:46:03.843472+00
39	37	kc3ThtOJkDicD1NloCUG38SxQg+5tbKoHcE1PcnorZs=	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Edg/151.0.0.0 Mobile Safari/537.36	154.72.161.110	Safari	Unknown	Android	Unknown	Android Device	\N	2026-08-29 17:50:38.84182+00	2026-08-29 17:50:38.841807+00
40	17	N1I3CudvwiScMPh3TWWF8UvbnqP9xTfEqIX1F8IgNjM=	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	154.72.161.110	Chrome	151	Android	Unknown	Android Device	\N	2026-08-29 17:55:14.772431+00	2026-08-29 17:55:14.77242+00
41	17	iLhugwQgI/7kd3k4pf742ZzPBmygNsuaQFxrBGWZRY8=	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	154.72.161.110	Safari	26.6	iOS		iPhone	\N	2026-08-29 19:38:53.80567+00	2026-08-29 19:38:53.80498+00
42	17	qarFycZec5EvdmxGO7CaIVT5Fz54+n4tWKj0c5XWBIA=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0	154.72.161.110	Chrome	151	Windows	10.0	Windows PC	\N	2026-08-29 19:41:44.40371+00	2026-08-29 19:41:44.403697+00
43	38	DYG26QtW1hK8TRDxhFn0jEyC8cFUmp4O/AawHAwts00=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	154.72.161.110	Chrome	151	Windows	10.0	Windows PC	\N	2026-08-29 20:17:45.83919+00	2026-08-29 20:17:45.838458+00
44	38	9fGr4dfIupypE781igxnieiVMp6i8rKNUIyhVB1pX8s=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	41.202.207.6	Chrome	152	Windows	10.0	Windows PC	\N	2026-08-30 19:57:46.184951+00	2026-08-30 19:57:46.184246+00
45	37	9fGr4dfIupypE781igxnieiVMp6i8rKNUIyhVB1pX8s=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	41.202.207.6	Chrome	152	Windows	10.0	Windows PC	\N	2026-08-30 20:09:23.924672+00	2026-08-30 20:09:23.924646+00
46	37	89z0PYZVTd2z22Kx1Tj/M2oQRsM+5RI/YgbXY+C3fes=	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	154.72.160.203	Safari	26.6	iOS		iPhone	\N	2026-08-30 20:13:01.911649+00	2026-08-30 20:13:01.911632+00
54	37	L1+bKPiC54aeHK/ws42Yq9PVOzt0UHwAJ9cnNA2SnVw=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	154.72.161.183	Chrome	151	Windows	10.0	Windows PC	\N	2026-09-01 06:36:52.634487+00	2026-09-01 06:36:52.634464+00
47	30	9fGr4dfIupypE781igxnieiVMp6i8rKNUIyhVB1pX8s=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	41.202.207.6	Chrome	152	Windows	10.0	Windows PC	\N	2026-08-31 03:37:04.075472+00	2026-08-31 03:16:27.128465+00
48	30	vgSaKnp0O4TH3PUhSksYQdW5FHZRgMp5Xb6vUc8ObpM=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	102.244.88.1	Chrome	151	Android	Unknown	Android Device	\N	2026-08-31 03:42:11.132931+00	2026-08-31 03:42:11.131911+00
49	38	OEjgnCMLktRx3M65uEI+dga/mtyKIyc6DxXclVZOMC8=	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	154.72.161.79	Chrome	151	Android	Unknown	Android Device	\N	2026-08-31 10:01:23.97849+00	2026-08-31 10:01:23.977812+00
50	38	5I3EYSeEa5q6PAFLfVqWp3HvEQ4zaYmRF5Iyc4Bd9sg=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	154.72.161.79	Chrome	151	Windows	10.0	Windows PC	\N	2026-08-31 11:40:18.419252+00	2026-08-31 11:40:18.418135+00
51	38	KkjBAv+zht9zIfeaCSA4RDj15Jg9e2ditkmVDQrQrMQ=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0	154.72.162.199	Chrome	151	Windows	10.0	Windows PC	\N	2026-08-31 13:18:10.470173+00	2026-08-31 13:18:10.469423+00
52	30	XiS2TqQzYFnto6+KNv8wYU+CuyTTbIMB7/5MCLGX4W4=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	41.202.207.16	Chrome	152	Windows	10.0	Windows PC	\N	2026-08-31 16:10:06.755378+00	2026-08-31 16:10:06.75469+00
55	39	IxxH5ZXZyoTzmO/fOtLebe8S3MB1k3FYh8SFstESOCc=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	129.0.76.122	Chrome	151	Android	Unknown	Android Device	\N	2026-09-01 09:15:39.857216+00	2026-09-01 09:15:39.856559+00
56	38	Q0ai1U8Mag9CrfWiEqVxvb6rSgWwpsU0MdapBZ1VRGU=	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Edg/152.0.0.0 Mobile Safari/537.36	154.72.161.183	Safari	Unknown	Android	Unknown	Android Device	\N	2026-09-01 13:36:42.471216+00	2026-09-01 13:36:42.470566+00
57	37	m3qqUv14gdCUX7NP0N6ogrkakQm35GCS3zWvaEpKSIo=	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	154.72.162.140	Chrome	151	Android	Unknown	Android Device	\N	2026-09-01 16:34:12.40073+00	2026-09-01 16:34:12.399996+00
58	38	7oVMnQKqZArEiihWaB3dG8Kc76KeAZ9f1v/GH2THC8I=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	154.72.162.140	Chrome	151	Windows	10.0	Windows PC	\N	2026-09-01 16:34:50.627599+00	2026-09-01 16:34:50.627587+00
59	17	RKuN8yzL658My9z92+7LMoQMat8AQx9HP9xMZR9TnjE=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	129.0.99.95	Chrome	151	Android	Unknown	Android Device	\N	2026-09-01 18:57:58.484723+00	2026-09-01 18:57:58.484037+00
60	38	RKuN8yzL658My9z92+7LMoQMat8AQx9HP9xMZR9TnjE=	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	129.0.99.95	Chrome	151	Android	Unknown	Android Device	\N	2026-09-01 19:13:17.616748+00	2026-09-01 19:13:17.616736+00
61	38	pBQoJBbJuKVtZ3Dh+raaXv6Qu7WQ/lwtzLwg5+Wu4f0=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	102.244.45.154	Chrome	152	Windows	10.0	Windows PC	\N	2026-09-01 21:58:09.824759+00	2026-09-01 21:58:09.823987+00
\.


--
-- Data for Name: DirectMessages; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."DirectMessages" ("Id", "FromUserId", "ToUserId", "Content", "IsRead", "ReadAt", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: DownloadHistories; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."DownloadHistories" ("Id", "UserId", "SubjectId", "ExamId", "FileName", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: EmailVerificationTokens; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."EmailVerificationTokens" ("Id", "UserId", "VerificationCode", "ExpiresAt", "IsVerified", "AttemptCount", "CreatedAt", "VerifiedAt", "Purpose") FROM stdin;
4	23	131734	2026-06-25 10:38:45.312614+00	f	0	2026-06-24 10:38:45.312577+00	\N	verify_email
6	25	545340	2026-08-13 12:27:51.331339+00	f	0	2026-08-12 12:27:51.331289+00	\N	verify_email
7	26	217664	2026-08-21 04:03:29.967675+00	f	0	2026-08-20 04:03:29.967625+00	\N	verify_email
11	30	731774	2026-08-21 06:52:36.599992+00	t	0	2026-08-20 06:52:36.599944+00	2026-08-20 06:53:43.194076+00	verify_email
12	31	270602	2026-08-21 10:43:26.290489+00	t	0	2026-08-20 10:43:26.290442+00	2026-08-20 10:43:57.990919+00	verify_email
17	36	235048	2026-08-26 15:38:32.807252+00	t	0	2026-08-25 15:38:32.807213+00	2026-08-25 15:44:25.316356+00	verify_email
18	37	312615	2026-08-28 09:13:04.568127+00	t	0	2026-08-27 09:13:04.568092+00	2026-08-27 09:13:45.749717+00	verify_email
20	39	633377	2026-09-02 08:56:17.710443+00	t	0	2026-09-01 08:56:17.710388+00	2026-09-01 08:56:59.569919+00	verify_email
21	17	834751	2026-09-01 19:08:38.688468+00	f	0	2026-09-01 18:58:38.688421+00	\N	periodic_confirm
\.


--
-- Data for Name: Enrollments; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Enrollments" ("Id", "UserId", "SubjectId", "EnrolledAt", "CompletedAt", "ProgressPercentage", "IsCompleted", "CertificateUrl", "IsDeleted") FROM stdin;
7	3	2	2025-12-05 13:09:19.135568+00	\N	100.00	t	\N	f
8	3	8	2025-12-25 13:09:19.135568+00	\N	50.00	f	\N	f
13	7	1	2025-12-22 13:09:19.135568+00	\N	100.00	t	\N	f
14	7	3	2026-01-01 13:09:19.135568+00	\N	65.00	f	\N	f
15	9	5	2026-01-01 13:09:19.135568+00	\N	80.00	f	\N	f
16	9	9	2026-01-21 13:09:19.135568+00	\N	15.00	f	\N	f
17	12	26	2026-01-19 13:09:19.135568+00	\N	55.00	f	\N	f
6	2	5	2025-12-30 13:09:19.135568+00	\N	75.00	f	\N	f
10	4	20	2026-01-04 13:09:19.135568+00	\N	30.00	f	\N	f
11	5	9	2025-12-15 13:09:19.135568+00	\N	50.00	f	\N	f
12	5	26	2026-01-14 13:09:19.135568+00	\N	45.00	f	\N	f
18	13	9	2026-01-24 13:09:19.135568+00	\N	70.00	f	\N	f
19	13	19	2026-02-03 13:09:19.135568+00	\N	50.00	f	\N	f
4	2	1	2025-12-02 13:09:19.135568+00	2026-02-15 16:00:00+00	100.00	t	https://certs.winplus.cm/cert/WPC-2026-0001.pdf	f
5	2	3	2025-12-10 13:09:19.135568+00	2026-02-10 14:00:00+00	100.00	t	https://certs.winplus.cm/cert/WPC-2026-0002.pdf	f
9	4	5	2025-12-08 13:09:19.135568+00	2026-02-12 11:00:00+00	100.00	t	https://certs.winplus.cm/cert/WPC-2026-0003.pdf	f
\.


--
-- Data for Name: Events; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Events" ("Id", "Title", "Description", "StartDate", "EndDate", "Location", "EventType", "TargetRole", "CreatedAt", "UpdatedAt") FROM stdin;
1	Examen Blanc Mathématiques	Simulation d'examen en conditions réelles pour le Bac.	2026-03-10 08:00:00+00	2026-03-10 12:00:00+00	En ligne	exam	student	2026-02-18 14:16:46.905346+00	\N
2	Réunion Parents-Enseignants	Point sur les progrès des élèves et objectifs du trimestre.	2026-03-12 17:00:00+00	2026-03-12 19:00:00+00	En ligne	meeting	parent	2026-02-18 14:16:46.905346+00	\N
3	Deadline - Rendu Projet Python	Date limite pour soumettre le projet final du cours Python.	2026-03-15 23:59:00+00	\N	En ligne	deadline	student	2026-02-18 14:16:46.905346+00	\N
4	Concours d'Informatique WinPlus	Compétition de programmation ouverte à tous les étudiants.	2026-03-20 09:00:00+00	2026-03-20 17:00:00+00	En ligne	class	student	2026-02-18 14:16:46.905346+00	\N
5	Webinaire - Orientation Post-Bac	Conseils pour choisir sa filière après le baccalauréat.	2026-03-22 14:00:00+00	2026-03-22 16:00:00+00	En ligne	class	parent	2026-02-18 14:16:46.905346+00	\N
6	Semaine des Langues	Une semaine dédiée aux langues étrangères avec ateliers et quiz.	2026-03-25 08:00:00+00	2026-03-29 18:00:00+00	En ligne	class	student	2026-02-18 14:16:46.905346+00	\N
\.


--
-- Data for Name: Exams; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Exams" ("Id", "Title", "ExamType", "SubjectId", "Category", "Year", "Session", "Level", "Duration", "DocumentUrl", "CorrectionUrl", "Description", "Difficulty", "DownloadCount", "IsPublished", "CreatedAt", "UpdatedAt", "IsDeleted", "ThumbnailUrl") FROM stdin;
1	BAC C - Mathématiques 2025	Baccalauréat	1	Mathématiques	2025	session_1	Terminale C	240	https://docs.winplus.cm/exams/bac/bac-c-maths-2025.pdf	https://docs.winplus.cm/exams/bac/bac-c-maths-2025-corrige.pdf	Épreuve de mathématiques du Baccalauréat série C session 2025.	difficile	1245	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
2	BAC C - Physique-Chimie 2025	Baccalauréat	2	Sciences	2025	session_1	Terminale C	180	https://docs.winplus.cm/exams/bac/bac-c-pc-2025.pdf	https://docs.winplus.cm/exams/bac/bac-c-pc-2025-corrige.pdf	Épreuve de physique-chimie du Baccalauréat série C session 2025.	difficile	1089	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
3	BAC A - Français 2025	Baccalauréat	3	Lettres	2025	session_1	Terminale A	240	https://docs.winplus.cm/exams/bac/bac-a-francais-2025.pdf	https://docs.winplus.cm/exams/bac/bac-a-francais-2025-corrige.pdf	Dissertation et commentaire composé - BAC série A 2025.	moyen	987	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
4	BAC A - Philosophie 2025	Baccalauréat	7	Philosophie	2025	session_1	Terminale A	240	https://docs.winplus.cm/exams/bac/bac-a-philo-2025.pdf	\N	Épreuve de philosophie BAC A 2025.	moyen	876	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
5	BAC C - SVT 2025	Baccalauréat	8	Sciences	2025	session_1	Terminale C	180	https://docs.winplus.cm/exams/bac/bac-c-svt-2025.pdf	https://docs.winplus.cm/exams/bac/bac-c-svt-2025-corrige.pdf	Sciences de la Vie et de la Terre - BAC C 2025.	moyen	756	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
6	BAC - Anglais 2025	Baccalauréat	5	Langues	2025	session_1	Toutes séries	180	https://docs.winplus.cm/exams/bac/bac-anglais-2025.pdf	https://docs.winplus.cm/exams/bac/bac-anglais-2025-corrige.pdf	Épreuve d'anglais toutes séries BAC 2025.	moyen	1345	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
7	BAC D - Mathématiques 2025	Baccalauréat	1	Mathématiques	2025	session_1	Terminale D	240	https://docs.winplus.cm/exams/bac/bac-d-maths-2025.pdf	https://docs.winplus.cm/exams/bac/bac-d-maths-2025-corrige.pdf	Épreuve de maths BAC série D 2025.	moyen	934	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
8	BAC - Histoire-Géographie 2025	Baccalauréat	4	Histoire	2025	session_1	Toutes séries	180	https://docs.winplus.cm/exams/bac/bac-hg-2025.pdf	\N	Histoire-Géographie BAC 2025.	moyen	678	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
9	BAC - Informatique 2025	Baccalauréat	9	Informatique	2025	session_1	Terminale C/D/TI	120	https://docs.winplus.cm/exams/bac/bac-info-2025.pdf	https://docs.winplus.cm/exams/bac/bac-info-2025-corrige.pdf	Épreuve d'informatique BAC 2025.	facile	567	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
10	BAC C - Mathématiques 2024	Baccalauréat	1	Mathématiques	2024	session_1	Terminale C	240	https://docs.winplus.cm/exams/bac/bac-c-maths-2024.pdf	https://docs.winplus.cm/exams/bac/bac-c-maths-2024-corrige.pdf	Épreuve de mathématiques BAC C 2024.	difficile	2345	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
11	BAC C - Physique-Chimie 2024	Baccalauréat	2	Sciences	2024	session_1	Terminale C	180	https://docs.winplus.cm/exams/bac/bac-c-pc-2024.pdf	https://docs.winplus.cm/exams/bac/bac-c-pc-2024-corrige.pdf	Physique-Chimie BAC C 2024.	difficile	1876	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
12	BAC A - Français 2024	Baccalauréat	3	Lettres	2024	session_1	Terminale A	240	https://docs.winplus.cm/exams/bac/bac-a-francais-2024.pdf	https://docs.winplus.cm/exams/bac/bac-a-francais-2024-corrige.pdf	Français BAC A 2024.	moyen	1567	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
13	BAC D - Mathématiques 2024	Baccalauréat	1	Mathématiques	2024	session_1	Terminale D	240	https://docs.winplus.cm/exams/bac/bac-d-maths-2024.pdf	https://docs.winplus.cm/exams/bac/bac-d-maths-2024-corrige.pdf	Maths BAC D 2024.	moyen	1678	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
14	BAC - Anglais 2024	Baccalauréat	5	Langues	2024	session_1	Toutes séries	180	https://docs.winplus.cm/exams/bac/bac-anglais-2024.pdf	https://docs.winplus.cm/exams/bac/bac-anglais-2024-corrige.pdf	Anglais BAC 2024.	moyen	1890	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
15	BAC C - Mathématiques 2023	Baccalauréat	1	Mathématiques	2023	session_1	Terminale C	240	https://docs.winplus.cm/exams/bac/bac-c-maths-2023.pdf	https://docs.winplus.cm/exams/bac/bac-c-maths-2023-corrige.pdf	Maths BAC C 2023.	difficile	3456	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
16	BAC A - Français 2023	Baccalauréat	3	Lettres	2023	session_1	Terminale A	240	https://docs.winplus.cm/exams/bac/bac-a-francais-2023.pdf	https://docs.winplus.cm/exams/bac/bac-a-francais-2023-corrige.pdf	Français BAC A 2023.	moyen	2345	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
17	Probatoire C - Mathématiques 2025	Probatoire	1	Mathématiques	2025	session_1	Première C	180	https://docs.winplus.cm/exams/probatoire/prob-c-maths-2025.pdf	https://docs.winplus.cm/exams/probatoire/prob-c-maths-2025-corrige.pdf	Épreuve de maths Probatoire série C 2025.	moyen	789	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
18	Probatoire C - Physique-Chimie 2025	Probatoire	2	Sciences	2025	session_1	Première C	150	https://docs.winplus.cm/exams/probatoire/prob-c-pc-2025.pdf	https://docs.winplus.cm/exams/probatoire/prob-c-pc-2025-corrige.pdf	Physique-Chimie Probatoire C 2025.	moyen	654	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
19	Probatoire A - Français 2025	Probatoire	3	Lettres	2025	session_1	Première A	180	https://docs.winplus.cm/exams/probatoire/prob-a-francais-2025.pdf	\N	Français Probatoire A 2025.	moyen	543	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
20	Probatoire D - SVT 2025	Probatoire	8	Sciences	2025	session_1	Première D	150	https://docs.winplus.cm/exams/probatoire/prob-d-svt-2025.pdf	https://docs.winplus.cm/exams/probatoire/prob-d-svt-2025-corrige.pdf	SVT Probatoire D 2025.	moyen	432	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
21	Probatoire - Anglais 2025	Probatoire	5	Langues	2025	session_1	Toutes séries	150	https://docs.winplus.cm/exams/probatoire/prob-anglais-2025.pdf	https://docs.winplus.cm/exams/probatoire/prob-anglais-2025-corrige.pdf	Anglais Probatoire 2025.	facile	567	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
22	Probatoire C - Mathématiques 2024	Probatoire	1	Mathématiques	2024	session_1	Première C	180	https://docs.winplus.cm/exams/probatoire/prob-c-maths-2024.pdf	https://docs.winplus.cm/exams/probatoire/prob-c-maths-2024-corrige.pdf	Maths Probatoire C 2024.	moyen	1234	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
23	Probatoire A - Français 2024	Probatoire	3	Lettres	2024	session_1	Première A	180	https://docs.winplus.cm/exams/probatoire/prob-a-francais-2024.pdf	https://docs.winplus.cm/exams/probatoire/prob-a-francais-2024-corrige.pdf	Français Probatoire A 2024.	moyen	987	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
24	BEPC - Mathématiques 2025	BEPC	1	Mathématiques	2025	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-maths-2025.pdf	https://docs.winplus.cm/exams/bepc/bepc-maths-2025-corrige.pdf	Épreuve de mathématiques BEPC 2025.	facile	2345	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
25	BEPC - Français 2025	BEPC	3	Lettres	2025	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-francais-2025.pdf	https://docs.winplus.cm/exams/bepc/bepc-francais-2025-corrige.pdf	Français BEPC 2025 - Dictée, rédaction, grammaire.	facile	2123	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
26	BEPC - Sciences 2025	BEPC	2	Sciences	2025	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-sciences-2025.pdf	https://docs.winplus.cm/exams/bepc/bepc-sciences-2025-corrige.pdf	Sciences (Physique + SVT) BEPC 2025.	facile	1876	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
27	BEPC - Anglais 2025	BEPC	5	Langues	2025	session_1	Troisième	90	https://docs.winplus.cm/exams/bepc/bepc-anglais-2025.pdf	https://docs.winplus.cm/exams/bepc/bepc-anglais-2025-corrige.pdf	Anglais BEPC 2025.	facile	1654	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
28	BEPC - Histoire-Géographie 2025	BEPC	4	Histoire	2025	session_1	Troisième	90	https://docs.winplus.cm/exams/bepc/bepc-hg-2025.pdf	\N	Histoire-Géographie BEPC 2025.	facile	1432	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
29	BEPC - Mathématiques 2024	BEPC	1	Mathématiques	2024	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-maths-2024.pdf	https://docs.winplus.cm/exams/bepc/bepc-maths-2024-corrige.pdf	Maths BEPC 2024.	facile	3456	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
30	BEPC - Français 2024	BEPC	3	Lettres	2024	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-francais-2024.pdf	https://docs.winplus.cm/exams/bepc/bepc-francais-2024-corrige.pdf	Français BEPC 2024.	facile	3123	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
31	BEPC - Sciences 2024	BEPC	2	Sciences	2024	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-sciences-2024.pdf	https://docs.winplus.cm/exams/bepc/bepc-sciences-2024-corrige.pdf	Sciences BEPC 2024.	facile	2876	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
32	ENS Yaoundé - Mathématiques 2025	ENS	1	Mathématiques	2025	session_1	1ère année	240	https://docs.winplus.cm/exams/ens/maths-2025.pdf	https://docs.winplus.cm/exams/ens/maths-2025-corrige.pdf	Concours ENS Yaoundé - Mathématiques 2025.	difficile	342	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
33	ENS Yaoundé - Mathématiques 2024	ENS	1	Mathématiques	2024	session_1	1ère année	240	https://docs.winplus.cm/exams/ens/maths-2024.pdf	https://docs.winplus.cm/exams/ens/maths-2024-corrige.pdf	Concours ENS Yaoundé - Mathématiques 2024.	difficile	567	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
34	ENS Yaoundé - Physique 2025	ENS	2	Sciences	2025	session_1	1ère année	180	https://docs.winplus.cm/exams/ens/physique-2025.pdf	https://docs.winplus.cm/exams/ens/physique-2025-corrige.pdf	Concours ENS - Physique 2025.	difficile	298	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
35	ENS Yaoundé - Informatique 2025	ENS	9	Informatique	2025	session_1	1ère année	180	https://docs.winplus.cm/exams/ens/info-2025.pdf	https://docs.winplus.cm/exams/ens/info-2025-corrige.pdf	Algorithmique et programmation ENS 2025.	difficile	412	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
36	ENS Yaoundé - Français 2025	ENS	3	Lettres	2025	session_1	1ère année	240	https://docs.winplus.cm/exams/ens/francais-2025.pdf	\N	Dissertation et commentaire composé ENS 2025.	moyen	234	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
37	Polytechnique - Mathématiques 2025	Polytechnique	1	Mathématiques	2025	session_1	Cycle ingénieur	300	https://docs.winplus.cm/exams/polytech/maths-2025.pdf	https://docs.winplus.cm/exams/polytech/maths-2025-corrige.pdf	Concours Polytechnique Yaoundé - Maths 2025.	difficile	623	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
38	Polytechnique - Mathématiques 2024	Polytechnique	1	Mathématiques	2024	session_1	Cycle ingénieur	300	https://docs.winplus.cm/exams/polytech/maths-2024.pdf	https://docs.winplus.cm/exams/polytech/maths-2024-corrige.pdf	Concours Polytechnique - Maths 2024.	difficile	812	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
39	Polytechnique - Physique 2025	Polytechnique	2	Sciences	2025	session_1	Cycle ingénieur	240	https://docs.winplus.cm/exams/polytech/physique-2025.pdf	https://docs.winplus.cm/exams/polytech/physique-2025-corrige.pdf	Physique Polytechnique 2025.	difficile	478	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
40	Polytechnique - Informatique 2025	Polytechnique	9	Informatique	2025	session_1	Cycle ingénieur	240	https://docs.winplus.cm/exams/polytech/info-2025.pdf	https://docs.winplus.cm/exams/polytech/info-2025-corrige.pdf	Informatique Polytechnique 2025.	difficile	389	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
41	ENAM - Culture Générale 2025	ENAM	\N	Lettres	2025	session_1	Division administrative	240	https://docs.winplus.cm/exams/enam/culture-gen-2025.pdf	\N	Culture générale ENAM 2025.	moyen	345	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
42	ENAM - Droit Civil 2025	ENAM	27	Droit	2025	session_1	Division judiciaire	240	https://docs.winplus.cm/exams/enam/droit-civil-2025.pdf	\N	Droit civil ENAM 2025.	difficile	278	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
43	FMSB - Biologie 2025	FMSB	8	Sciences	2025	session_1	1ère année médecine	180	https://docs.winplus.cm/exams/fmsb/bio-2025.pdf	https://docs.winplus.cm/exams/fmsb/bio-2025-corrige.pdf	Biologie concours médecine 2025.	difficile	567	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
44	FMSB - Chimie 2025	FMSB	12	Sciences	2025	session_1	1ère année médecine	180	https://docs.winplus.cm/exams/fmsb/chimie-2025.pdf	\N	Chimie organique FMSB 2025.	difficile	445	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
45	FMSB - Physique 2025	FMSB	2	Sciences	2025	session_1	1ère année médecine	180	https://docs.winplus.cm/exams/fmsb/physique-2025.pdf	https://docs.winplus.cm/exams/fmsb/physique-2025-corrige.pdf	Biophysique FMSB 2025.	difficile	389	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
46	ESSEC - Maths Financières 2025	ESSEC	16	Mathématiques	2025	session_1	Licence 1	180	https://docs.winplus.cm/exams/essec/maths-fin-2025.pdf	https://docs.winplus.cm/exams/essec/maths-fin-2025-corrige.pdf	Maths financières ESSEC 2025.	moyen	289	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
47	ENSET - Informatique 2025	ENSET	9	Informatique	2025	session_1	1ère année	180	https://docs.winplus.cm/exams/enset/info-2025.pdf	https://docs.winplus.cm/exams/enset/info-2025-corrige.pdf	Informatique ENSET 2025.	moyen	312	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
48	IUT Douala - Informatique 2025	IUT	9	Informatique	2025	session_1	DUT	120	https://docs.winplus.cm/exams/iut/info-2025.pdf	https://docs.winplus.cm/exams/iut/info-2025-corrige.pdf	Programmation et BD IUT 2025.	facile	198	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f	\N
\.


--
-- Data for Name: FavoriteCollections; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."FavoriteCollections" ("Id", "UserId", "Name", "Description", "Color", "Icon", "Order", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: Favorites; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Favorites" ("Id", "UserId", "SubjectId", "AddedAt", "CollectionId") FROM stdin;
4	2	9	2025-12-15 13:09:19.139554+00	\N
5	2	19	2026-01-04 13:09:19.139554+00	\N
6	3	1	2025-12-20 13:09:19.139554+00	\N
7	4	26	2025-12-25 13:09:19.139554+00	\N
8	5	19	2025-12-30 13:09:19.139554+00	\N
9	7	9	2026-01-09 13:09:19.139554+00	\N
10	9	5	2026-01-14 13:09:19.139554+00	\N
\.


--
-- Data for Name: FocusSessions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."FocusSessions" ("Id", "UserId", "PlannedDurationSeconds", "ActualDurationSeconds", "Label", "StartedAt", "CompletedAt") FROM stdin;
\.


--
-- Data for Name: ForumModerationQueue; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ForumModerationQueue" ("Id", "PostId", "ThreadId", "ContentText", "AuthorId", "Verdict", "Confidence", "Reason", "Status", "CreatedAt", "ResolvedAt", "AdminNote") FROM stdin;
\.


--
-- Data for Name: ForumPosts; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ForumPosts" ("Id", "ThreadId", "UserId", "Content", "Upvotes", "IsAccepted", "CreatedAt", "UpdatedAt", "IsDeleted", "IsHidden") FROM stdin;
1	1	30	salut\nc'est une vrai question	0	f	2026-08-20 07:27:53.107344+00	2026-08-20 07:27:53.107364+00	f	f
2	1	37	Je ne comprend pas	0	f	2026-08-30 22:11:58.735895+00	2026-08-30 22:11:58.735914+00	f	f
3	1	37	Tes noyaux	0	f	2026-08-30 22:12:18.53352+00	2026-08-30 22:12:18.53352+00	f	f
4	2	37	J ene pense pas que se soit vrai	0	f	2026-08-31 02:31:09.210591+00	2026-08-31 02:31:09.21064+00	f	f
5	2	37	ets ce que si je parle de meutre on va me sensurer	0	f	2026-08-31 02:32:05.532335+00	2026-08-31 02:32:05.532336+00	f	f
6	2	37	merde je ne comprend pas le principe	0	f	2026-08-31 02:32:24.783654+00	2026-08-31 02:32:24.783654+00	f	f
\.


--
-- Data for Name: ForumThreadFollows; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ForumThreadFollows" ("Id", "UserId", "ThreadId", "CreatedAt") FROM stdin;
1	37	2	2026-08-30 22:11:47.554405+00
\.


--
-- Data for Name: ForumThreads; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ForumThreads" ("Id", "UserId", "Title", "Content", "Category", "Tag", "IsPinned", "IsSolved", "ViewsCount", "RepliesCount", "Upvotes", "CreatedAt", "UpdatedAt", "IsDeleted") FROM stdin;
2	37	La bosse	Je teste juste en passant par là ?	Questions	Maths	f	f	3	3	0	2026-08-30 22:11:39.400556+00	2026-08-31 02:32:24.783928+00	f
1	30	L'espace latent	Que pensez vous de l'espace latent	Discussions	Machine learning	f	f	12	3	0	2026-08-20 07:26:21.66534+00	2026-08-30 22:12:18.534232+00	f
\.


--
-- Data for Name: ForumVotes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ForumVotes" ("Id", "PostId", "UserId", "Type", "CreatedAt", "Weight") FROM stdin;
\.


--
-- Data for Name: Goals; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Goals" ("Id", "UserId", "Title", "Description", "Type", "TargetDate", "Status", "Progress", "CreatedAt", "UpdatedAt") FROM stdin;
4	2	Réussir le BAC A	Obtenir au moins 14/20 de moyenne au BAC A.	exam	2026-06-15 00:00:00+00	in_progress	70.00	2026-02-20 15:01:27.52467+00	2026-02-20 15:01:27.52467+00
5	2	Maîtriser la dissertation	Obtenir régulièrement 16+ en dissertation.	skill	2026-03-15 00:00:00+00	completed	100.00	2026-02-20 15:01:27.52467+00	2026-02-20 15:01:27.52467+00
6	4	TOEFL score 90+	Atteindre un score de 90 au TOEFL pour postuler à l'étranger.	certification	2026-09-01 00:00:00+00	in_progress	55.00	2026-02-20 15:01:27.52467+00	2026-02-20 15:01:27.52467+00
7	4	Certification Marketing Digital	Compléter le cours de marketing digital avec certificat.	certification	2026-04-30 00:00:00+00	in_progress	30.00	2026-02-20 15:01:27.52467+00	2026-02-20 15:01:27.52467+00
8	5	Suivre les progrès de mon enfant	Vérifier chaque semaine les statistiques de progression.	parenting	\N	in_progress	60.00	2026-02-20 15:01:27.52467+00	2026-02-20 15:01:27.52467+00
9	13	Concours ENS Informatique	Se préparer au concours d'entrée ENS filière informatique.	exam	2026-07-01 00:00:00+00	in_progress	35.00	2026-02-20 15:01:27.52467+00	2026-02-20 15:01:27.52467+00
10	13	Portfolio de projets	Créer 5 projets personnels en Python et JavaScript.	skill	2026-06-01 00:00:00+00	in_progress	20.00	2026-02-20 15:01:27.52467+00	2026-02-20 15:01:27.52467+00
\.


--
-- Data for Name: HomePageFeatures; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."HomePageFeatures" ("Id", "Title", "Description", "Icon", "ImageUrl", "Order", "IsActive", "CreatedAt", "UpdatedAt") FROM stdin;
1	Épreuves Officielles Gratuites	Accédez gratuitement aux épreuves du BAC, Probatoire, BEPC et concours d'entrée aux grandes écoles avec corrigés détaillés.	FileText	https://images.winplus.cm/features/exams.png	1	t	2026-02-20 15:00:40.755626+00	2026-02-20 15:00:40.755626+00
2	Quiz Adaptatifs par IA	Notre intelligence artificielle génère des quiz personnalisés selon votre niveau et vos lacunes pour une progression optimale.	Brain	https://images.winplus.cm/features/quiz-ai.png	2	t	2026-02-20 15:00:40.755626+00	2026-02-20 15:00:40.755626+00
3	Fiches de Révision Complètes	Des fiches synthétiques et structurées pour chaque matière, créées par des enseignants expérimentés du Cameroun.	BookOpen	https://images.winplus.cm/features/revisions.png	3	t	2026-02-20 15:00:40.755626+00	2026-02-20 15:00:40.755626+00
4	Statistiques Détaillées	Suivez votre progression en temps réel avec des graphiques et des métriques précises. Les parents peuvent aussi suivre leurs enfants.	BarChart3	https://images.winplus.cm/features/stats.png	4	t	2026-02-20 15:00:40.755626+00	2026-02-20 15:00:40.755626+00
5	Sessions Live avec des Profs	Participez à des cours en direct avec des enseignants qualifiés. Posez vos questions et obtenez des réponses instantanées.	Video	https://images.winplus.cm/features/live-sessions.png	5	t	2026-02-20 15:00:40.755626+00	2026-02-20 15:00:40.755626+00
6	Certificats de Réussite	Obtenez des certificats officiels WinPlus à chaque cours complété pour valoriser votre parcours d'apprentissage.	Award	https://images.winplus.cm/features/certificates.png	6	t	2026-02-20 15:00:40.755626+00	2026-02-20 15:00:40.755626+00
\.


--
-- Data for Name: InstitutionStudents; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."InstitutionStudents" ("Id", "InstitutionId", "StudentId", "GroupName", "Level", "MatriculeNumber", "IsActive", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: Institutions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Institutions" ("Id", "Name", "Code", "Country", "City", "Region", "Type", "IsActive", "CreatedAt", "UpdatedAt", "IsDeleted", "Email", "Phone", "Address") FROM stdin;
12	Institut Universitaire de Technologie de Douala	IUT-D	CM	Douala	Littoral	School	t	2026-02-18 14:16:46.896311+00	\N	f	\N	\N	\N
14	IRIC Yaoundé	IRIC	CM	Yaoundé	Centre	School	t	2026-02-18 14:16:46.896311+00	\N	f	\N	\N	\N
16	Université des Montagnes	UdM	CM	Bangangté	Ouest	University	t	2026-02-18 14:16:46.896311+00	\N	f	\N	\N	\N
17	Université Protestante d'Afrique Centrale	UPAC	CM	Yaoundé	Centre	University	t	2026-02-18 14:16:46.896311+00	\N	f	\N	\N	\N
18	Institut Siantou Supérieur	ISS	CM	Yaoundé	Centre	College	t	2026-02-18 14:16:46.896311+00	\N	f	\N	\N	\N
19	Pigier Cameroun	PIGIER	CM	Douala	Littoral	College	t	2026-02-18 14:16:46.896311+00	\N	f	\N	\N	\N
20	IUC (Institut Universitaire de la Côte)	IUC	CM	Douala	Littoral	College	t	2026-02-18 14:16:46.896311+00	\N	f	\N	\N	\N
22	Lycée de la Cité Verte	LCV	CM	Yaoundé	Centre	School	t	2026-02-18 14:16:46.896311+00	\N	f	\N	\N	\N
24	Lycée Joss de Douala	LJOSS	CM	Douala	Littoral	School	t	2026-02-18 14:16:46.896311+00	\N	f	\N	\N	\N
25	Lycée Bilingue de Buea	LBB	CM	Buea	Sud-Ouest	School	t	2026-02-18 14:16:46.896311+00	\N	f	\N	\N	\N
1	Université de Yaoundé I	UY1	CM	Yaoundé	Centre	University	t	2026-02-18 14:16:46.896311+00	\N	f	info@univ-yaounde1.cm	+237 222 220 744	BP 337, Yaoundé, Cameroun
2	Université de Yaoundé II - Soa	UY2	CM	Yaoundé	Centre	University	t	2026-02-18 14:16:46.896311+00	\N	f	info@univ-yaounde2.cm	+237 222 213 041	BP 18, Soa, Yaoundé, Cameroun
3	Université de Douala	UD	CM	Douala	Littoral	University	t	2026-02-18 14:16:46.896311+00	\N	f	info@univ-douala.cm	+237 233 401 135	BP 2701, Douala, Cameroun
4	Université de Dschang	UDs	CM	Dschang	Ouest	University	t	2026-02-18 14:16:46.896311+00	\N	f	info@univ-dschang.cm	+237 233 451 381	BP 96, Dschang, Cameroun
5	Université de Buea	UB	CM	Buea	Sud-Ouest	University	t	2026-02-18 14:16:46.896311+00	\N	f	info@ubuea.cm	+237 233 322 134	P.O. Box 63, Buea, Cameroun
6	Université de Ngaoundéré	UN	CM	Ngaoundéré	Adamaoua	University	t	2026-02-18 14:16:46.896311+00	\N	f	info@univ-ngaoundere.cm	+237 222 254 112	BP 454, Ngaoundéré, Cameroun
7	Université de Maroua	UM	CM	Maroua	Extrême-Nord	University	t	2026-02-18 14:16:46.896311+00	\N	f	info@univ-maroua.cm	+237 222 291 541	BP 814, Maroua, Cameroun
8	Université de Bamenda	UBa	CM	Bamenda	Nord-Ouest	University	t	2026-02-18 14:16:46.896311+00	\N	f	info@univ-bamenda.cm	+237 233 362 008	BP 39, Bambili, Bamenda, Cameroun
9	École Normale Supérieure de Yaoundé	ENS-Y	CM	Yaoundé	Centre	School	t	2026-02-18 14:16:46.896311+00	\N	f	ens@univ-yaounde1.cm	+237 222 223 568	BP 47, Yaoundé, Cameroun
10	École Polytechnique de Yaoundé	ENSP	CM	Yaoundé	Centre	School	t	2026-02-18 14:16:46.896311+00	\N	f	ensp@univ-yaounde1.cm	+237 222 223 012	BP 8390, Yaoundé, Cameroun
11	ENSET Douala	ENSET-D	CM	Douala	Littoral	School	t	2026-02-18 14:16:46.896311+00	\N	f	info@ensetdouala.cm	+237 233 401 867	BP 1872, Douala, Cameroun
15	Université Catholique d'Afrique Centrale	UCAC	CM	Yaoundé	Centre	University	t	2026-02-18 14:16:46.896311+00	\N	f	info@ucac-icy.net	+237 222 305 585	BP 11628, Yaoundé, Cameroun
13	ESSEC Douala	ESSEC	CM	Douala	Littoral	School	t	2026-02-18 14:16:46.896311+00	\N	f	info@essec-douala.cm	+237 233 428 602	BP 1931, Douala, Cameroun
21	Lycée Général Leclerc	LGL	CM	Yaoundé	Centre	School	t	2026-02-18 14:16:46.896311+00	\N	f	info@lgl.cm	+237 222 222 150	Avenue Kennedy, Yaoundé, Cameroun
23	Collège Vogt	VOGT	CM	Yaoundé	Centre	School	t	2026-02-18 14:16:46.896311+00	\N	f	info@collegevogt.cm	+237 222 221 234	Mvolyé, Yaoundé, Cameroun
\.


--
-- Data for Name: LearningHistories; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."LearningHistories" ("Id", "UserId", "SubjectId", "ContentId", "ActivityType", "TimeSpentSeconds", "QuizScore", "ActivityAt", "CreatedAt", "DurationSeconds", "EventDescription", "EventDetails", "EventTitle", "EventType", "IsCompleted", "Notes", "ProgressPercentage", "Score", "UpdatedAt") FROM stdin;
26	2	1	3	lesson_view	2700	\N	2026-02-08 10:00:00+00	2026-02-08 10:00:00+00	\N	\N	\N	Introduction aux Limites	lesson	t	\N	100	0	2026-02-08 10:45:00+00
27	2	3	11	lesson_view	2400	\N	2026-02-09 14:00:00+00	2026-02-09 14:00:00+00	\N	\N	\N	Méthodologie de la Dissertation	lesson	t	\N	100	0	2026-02-09 14:40:00+00
28	2	3	12	lesson_view	2100	\N	2026-02-10 14:00:00+00	2026-02-10 14:00:00+00	\N	\N	\N	Construire un Plan	lesson	t	\N	100	0	2026-02-10 14:35:00+00
29	2	5	15	lesson_view	1800	\N	2026-02-12 09:00:00+00	2026-02-12 09:00:00+00	\N	\N	\N	Greetings & Introductions	lesson	t	\N	100	0	2026-02-12 09:30:00+00
30	4	5	15	lesson_view	1800	\N	2026-02-07 10:00:00+00	2026-02-07 10:00:00+00	\N	\N	\N	Greetings & Introductions	lesson	t	\N	100	0	2026-02-07 10:30:00+00
31	4	5	16	lesson_view	2100	\N	2026-02-09 10:00:00+00	2026-02-09 10:00:00+00	\N	\N	\N	Everyday Conversations	lesson	t	\N	100	0	2026-02-09 10:35:00+00
32	4	20	27	lesson_view	2400	\N	2026-02-14 15:00:00+00	2026-02-14 15:00:00+00	\N	\N	\N	Introduction au Marketing Digital	lesson	f	\N	30	0	2026-02-14 15:40:00+00
33	13	9	18	lesson_view	2700	\N	2026-02-11 08:00:00+00	2026-02-11 08:00:00+00	\N	\N	\N	Introduction à Python	lesson	t	\N	100	0	2026-02-11 08:45:00+00
34	13	9	19	lesson_view	3000	\N	2026-02-12 08:00:00+00	2026-02-12 08:00:00+00	\N	\N	\N	Structures de Contrôle	lesson	t	\N	100	0	2026-02-12 08:50:00+00
35	13	9	20	quiz_attempt	540	92.00	2026-02-13 09:00:00+00	2026-02-13 09:00:00+00	\N	\N	\N	Quiz - Fonctions Python	quiz	t	\N	100	92	2026-02-13 09:09:00+00
36	13	19	\N	lesson_view	2400	\N	2026-02-15 14:00:00+00	2026-02-15 14:00:00+00	\N	\N	\N	Closures et Prototypes JS	lesson	f	\N	50	0	2026-02-15 14:40:00+00
\.


--
-- Data for Name: LessonProgress; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."LessonProgress" ("Id", "UserId", "LessonId", "CourseId", "IsCompleted", "WatchTimeSec", "LastPositionSec", "CompletedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: Levels; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Levels" ("Id", "Name", "DisplayName", "Description", "Order", "IsActive", "CreatedAt", "UpdatedAt") FROM stdin;
1	primaire	Primaire	Cycle primaire (CM1-CM2)	1	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
2	college	Collège	Cycle collège (6ème - 3ème)	2	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
3	troisieme	Troisième	Classe de 3ème - Préparation BEPC	3	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
4	seconde	Seconde	Classe de Seconde	4	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
5	premiere	Première	Classe de Première - Préparation Probatoire	5	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
6	terminale	Terminale	Classe de Terminale - Préparation BAC	6	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
7	terminale_a	Terminale A	Série littéraire	7	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
8	terminale_c	Terminale C	Série scientifique (Maths-Physique)	8	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
9	terminale_d	Terminale D	Série scientifique (Sciences naturelles)	9	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
10	terminale_ti	Terminale TI	Série Technique Industrielle	10	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
11	premiere_c	Première C	Première scientifique Maths-Physique	11	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
12	premiere_d	Première D	Première scientifique SVT	12	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
13	premiere_a	Première A	Première littéraire	13	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
14	licence_1	Licence 1	Première année universitaire	14	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
15	licence_2	Licence 2	Deuxième année universitaire	15	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
16	licence_3	Licence 3	Troisième année universitaire	16	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
17	master_1	Master 1	Première année de Master	17	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
18	master_2	Master 2	Deuxième année de Master	18	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
19	dut	DUT	Diplôme Universitaire de Technologie	19	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
20	bts	BTS	Brevet de Technicien Supérieur	20	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
21	cycle_ingenieur	Cycle Ingénieur	Formation d'ingénieur (grandes écoles)	21	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
22	medecine_1	1ère année Médecine	Première année de médecine (FMSB)	22	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
23	concours	Préparation Concours	Préparation aux concours d'entrée	23	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
24	formation_pro	Formation Professionnelle	Formation continue et professionnelle	24	t	2026-02-20 14:59:33.730864+00	2026-02-20 14:59:33.730864+00
\.


--
-- Data for Name: Messages; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Messages" ("Id", "ConversationId", "Role", "Content", "TokensUsed", "CreatedAt", "IsDeleted", "IsEdited", "UpdatedAt", "Attachments", "FeedbackRating", "FeedbackComment", "GenerationTimeMs") FROM stdin;
1	1	user	Bonjour	\N	2026-08-30 22:12:32.720731+00	f	f	\N	\N	\N	\N	\N
2	1	user	Aide-moi à planifier mes révisions pour le concours ENSP dans 2 mois, j'ai 3h par jour disponibles	\N	2026-08-30 22:12:40.132624+00	f	f	\N	\N	\N	\N	\N
3	1	user	Analyse mes résultats récents et identifie mes 3 principales lacunes en mathématiques	\N	2026-08-30 22:12:45.53886+00	f	f	\N	\N	\N	\N	\N
4	2	user	Bonjour	\N	2026-08-30 22:35:27.499294+00	f	f	\N	\N	\N	\N	\N
5	2	user	Interpète moi ce graphique	\N	2026-08-30 22:35:54.535882+00	f	f	\N	\N	\N	\N	\N
6	2	user	Salut	\N	2026-08-31 01:56:20.319922+00	f	f	\N	\N	\N	\N	\N
7	3	user	Bonjour	\N	2026-08-31 01:58:24.527394+00	f	f	\N	\N	\N	\N	\N
8	4	user	Bonjour	\N	2026-08-31 02:45:00.085809+00	f	f	\N	\N	\N	\N	\N
9	5	user	Résume ma progression	\N	2026-08-31 03:16:46.056432+00	f	f	\N	\N	\N	\N	\N
10	5	user	Bonjour	\N	2026-08-31 03:17:12.707064+00	f	f	\N	\N	\N	\N	\N
11	5	user	Salut	\N	2026-08-31 03:26:45.200144+00	f	f	\N	\N	\N	\N	\N
12	5	user	Salut	\N	2026-08-31 03:27:07.003196+00	f	f	\N	\N	\N	\N	\N
13	6	user	Résume ma progression	\N	2026-08-31 03:31:32.96917+00	f	f	\N	\N	\N	\N	\N
14	6	user	Bonjour	\N	2026-08-31 03:31:43.838102+00	f	f	\N	\N	\N	\N	\N
15	6	user	Salut	\N	2026-08-31 03:33:25.358356+00	f	f	\N	\N	\N	\N	\N
16	7	user	Quiz surprise sur mes lacunes	\N	2026-08-31 03:37:10.45792+00	f	f	\N	\N	\N	\N	\N
17	7	assistant	Excellent ! 🎯 Un quiz surprise, c'est une super idée pour voir où tu en es et cibler ce qu'on doit travailler ensemble. Je suis prêt à t'aider.\n\nPour que ce soit le plus utile possible, dis-moi d'abord :\n1. **Quelle matière** veux-tu réviser ? (Maths, Physique, Chimie, Français, Histoire, Anglais, SVT, autre...)\n2. **Quel niveau** ? (Collège, Lycée, Supérieur...)\n3. **Quel thème** en particulier, ou je choisis au hasard dans le programme ?\n\nDès que tu me donnes ça, je te lance **5 questions progressives** (du plus simple au plus corsé). On corrigera ensemble, étape par étape, sans te donner les réponses directement. C'est toi qui trouves, je t'accompagne ! 💪\n\nAlors, on commence par quoi ?	0	2026-08-31 03:37:13.852404+00	f	f	\N	\N	\N	\N	3228
18	7	user	Interprète ce graphique	\N	2026-08-31 03:39:00.329213+00	f	f	\N	\N	\N	\N	\N
19	7	assistant	Très bien, passons à l'interprétation de graphique ! 📊\n\nPour que je puisse t'aider efficacement, il me faut **voir le graphique** dont tu parles. Comme je ne peux pas voir d'image directement, voici ce que tu peux faire :\n\n1. **Décris-le moi** (titre, axes, type de courbe, valeurs clés...)\n2. **Ou copie-colle les données** s'il s'agit d'un tableau ou d'un graphique numérique\n3. **Ou dis-moi de quel type il s'agit** (courbe, histogramme, camembert, nuage de points...)\n\nEn attendant, voici une **trame d'analyse** que tu peux suivre pour tout graphique :\n\n---\n\n**📌 Méthode en 4 étapes :**\n1. **Identifier** : Titre, axes (X et Y), unités, légende.\n2. **Décrire** : Tendance générale (croissance, décroissance, stagnation, variations).\n3. **Repérer** : Points particuliers (maximum, minimum, rupture de pente, palier).\n4. **Interpréter** : Que signifie cette tendance dans le contexte ? Quelle conclusion en tirer ?\n\n---\n\n**Exemple concret :**\n> *Graphique : Évolution de la température moyenne à Paris de 1900 à 2020.*\n> - **Identifier** : Axe X = années, Axe Y = température (°C).\n> - **Décrire** : Tendance globale à la hausse, avec des oscillations.\n> - **Repérer** : Forte accélération après 1980.\n> - **Interpréter** : Réchauffement climatique, avec une accentuation récente.\n\n---\n\nAlors, décris-moi ton graphique, et je t'aide à l'interpréter pas à pas ! 🚀	0	2026-08-31 03:39:05.180964+00	f	f	\N	\N	\N	\N	4801
20	8	user	Salut	\N	2026-08-31 03:42:19.981188+00	f	f	\N	\N	\N	\N	\N
21	8	assistant	Salut ! 👋 Ravi de te voir ici. Je suis **WinAI**, ton assistant IA de WinPlus.  \nDis-moi sur quoi tu veux travailler : un exercice, une notion à revoir, ou peut-être une fiche de révision à créer ? On y va ensemble, à ton rythme ! 😊	0	2026-08-31 03:42:21.48712+00	f	f	\N	\N	\N	\N	1453
22	9	user	Tu explores le catalogue  je peux te recommander les sujets qui correspondent à ton profil. On regarde ensemble ?	\N	2026-08-31 03:47:01.768224+00	f	f	\N	\N	\N	\N	\N
23	9	assistant	Bien sûr, on peut regarder ensemble ! 😊  \nPour que je puisse te recommander des sujets adaptés à ton profil, peux-tu me dire :  \n- **Quelle matière** t’intéresse (maths, physique, français, histoire, etc.) ?  \n- **Ton niveau** (collège, lycée, études supérieures, etc.) ?  \n- **Un objectif** (préparer un examen, combler des lacunes, approfondir, ou juste découvrir) ?  \n\nAvec ça, je te propose un petit parcours sur mesure. Et si tu préfères, on peut aussi commencer par un **mini-test** pour cerner tes points forts et tes axes de progrès. Tu veux essayer ?	0	2026-08-31 03:47:04.141247+00	f	f	\N	\N	\N	\N	2295
24	10	user	Bonjour	\N	2026-08-31 16:10:21.364231+00	f	f	\N	\N	\N	\N	\N
25	10	assistant	Bonjour ! 😊 Je suis WinAI, ton assistant IA de WinPlus. Je suis là pour t’aider à réviser, comprendre et progresser, que ce soit en maths, en français, en sciences ou dans toute autre matière.\n\nDis-moi ce dont tu as besoin aujourd’hui :\n- une explication sur une notion qui te bloque ?\n- un exercice ou un quiz pour t’entraîner ?\n- une fiche de révision ou un mémento ?\n- ou bien tu veux qu’on corrige un devoir ensemble ?\n\nJe m’adapte à ton niveau et à tes lacunes pour que tout soit clair et efficace. Alors, par où veux-tu commencer ? 🚀	0	2026-08-31 16:10:24.107431+00	f	f	\N	\N	\N	\N	2472
26	10	user	Interprète ce graphique	\N	2026-08-31 16:10:48.717968+00	f	f	\N	\N	\N	\N	\N
27	10	user	Analyse mes résultats récents et identifie mes 3 principales lacunes en mathématiques	\N	2026-08-31 16:11:02.402247+00	f	f	\N	\N	\N	\N	\N
28	10	assistant	Bien sûr ! Je vais t'aider à interpréter ce graphique et analyser tes résultats. Cependant, je n'ai pas encore reçu le graphique en question (ni les données chiffrées). Peux-tu **téléverser l'image** ou **me coller les valeurs** (notes, pourcentages, thèmes) ?\n\nEn attendant, voici comment on va procéder dès que je les aurai :\n\n1. **Repérer les thèmes** les plus faibles (notes en dessous de la moyenne ou tendance à la baisse).\n2. **Croiser avec tes lacunes connues** (par exemple : fractions, fonctions, géométrie…).\n3. **Te proposer un plan ciblé** : exercices, fiches, quiz pour combler ces lacunes.\n\n📌 **Exemple d'analyse que je pourrai te donner** (une fois les données reçues) :\n- **Lacune n°1** : Calcul littéral (notes entre 4/10 et 6/10) → *on reverra les identités remarquables*\n- **Lacune n°2** : Probabilités (chute de 7 à 3/10) → *on fera des exercices sur les arbres pondérés*\n- **Lacune n°3** : Trigonométrie (stagnation à 5/10) → *on révisera les formules du cosinus/sinus*\n\n👉 **Envoie-moi le graphique ou les chiffres**, et je te fais l'analyse complète tout de suite !	0	2026-08-31 16:11:06.60917+00	f	f	\N	\N	\N	\N	4115
29	11	user	Bonne journée paolo ! Ta session du jour : révision de tes cours. Commencer ?	\N	2026-09-01 08:58:49.666129+00	f	f	\N	\N	\N	\N	\N
30	11	assistant	Bonjour Paolo ! 😊 Quelle belle journée pour réviser ! Je suis WinAI, ton assistant IA de WinPlus, et je suis prêt à t'accompagner.\n\nOui, commençons ! Pour bien démarrer, je te propose de faire un petit point rapide sur ce que tu as vu récemment. Ensuite, on attaque directement avec une question ou un exercice ciblé — selon ce qui te ferait le plus progresser aujourd'hui.\n\nDis-moi simplement : tu préfères qu'on fasse une **révision active** (questions/réponses), un **exercice guidé** ou une **fiche de synthèse** sur un chapitre précis ? On y va ! 💪	0	2026-09-01 08:58:52.319347+00	f	f	\N	\N	\N	\N	2396
31	11	user	aide moi à rédiger une lettre pour un changement de lycée ( lettre de demande)	\N	2026-09-01 09:00:29.31183+00	f	f	\N	\N	\N	\N	\N
32	11	assistant	Bien sûr Paolo ! Je t'aide à rédiger ta lettre de demande de changement de lycée. C'est une démarche importante, donc on va la construire ensemble, étape par étape, pour qu'elle soit claire et convaincante.\n\nAvant de commencer, je te propose un **plan type** qu'on va remplir ensemble. Mais d'abord, dis-moi juste :\n\n1. **Quel est ton niveau** (Seconde, Première, Terminale) ? *(Je peux le deviner selon ton profil, mais précise si tu veux une version adaptée)*\n2. **Pourquoi ce changement ?** (déménagement, projet scolaire précis, orientation, raison personnelle...)\n3. **Vers quel type de lycée** tu souhaites aller ? (général, technologique, professionnel, avec une option spécifique...)\n\n---\n\nEn attendant, voici un **modèle type** que tu pourras adapter. Je te le donne déjà pour que tu gagnes du temps :\n\n---\n\n**Objet : Demande de changement d'établissement scolaire**\n\nMadame, Monsieur le/la Proviseur(e),\n\nActuellement élève en [classe] au lycée [nom de ton lycée actuel], je me permets de vous adresser la présente afin de solliciter mon inscription dans votre établissement pour l'année scolaire [année].\n\nCe souhait de changement s'explique par [raison principale : déménagement / projet d'orientation / raison personnelle]. En effet, [développe ta raison en 2-3 phrases : par exemple, "ma famille doit déménager dans votre secteur", ou "je souhaite intégrer la filière [filière] que propose votre lycée afin de concrétiser mon projet professionnel"].\n\nActuellement, j'obtiens [résultats / appréciations générales] et je suis particulièrement motivé(e) par [matières ou projets]. Je suis convaincu(e) que votre établissement, par son [réputation, options, encadrement], me permettra de m'épanouir et de réussir ma scolarité.\n\nJe reste à votre disposition pour tout entretien ou information complémentaire. Je vous prie d'agréer, Madame, Monsieur le/la Proviseur(e), l'expression de ma considération distinguée.\n\n[Signature]\n[Prénom NOM]\n[Classe actuelle]\n[Coordonnées : téléphone, email]\n\n---\n\n**Dis-moi tes infos** (raison, niveau, lycée visé) et je personnalise cette lettre avec toi, phrase par phrase, pour qu'elle soit parfaite. On y va ! ✍️	0	2026-09-01 09:00:36.806665+00	f	f	\N	\N	\N	\N	7398
33	12	user	Tu explores le catalogue  je peux te recommander les sujets qui correspondent à ton profil. On regarde ensemble ?	\N	2026-09-01 09:04:49.749923+00	f	f	\N	\N	\N	\N	\N
34	12	assistant	Bien sûr, on regarde ensemble ! 🎉\n\nJe vois que tu préfères choisir entre **révision active**, **exercice guidé** ou **fiche de synthèse**. On peut donc explorer le catalogue en fonction de ce que tu veux faire aujourd'hui.\n\nPour t'aider à démarrer, dis-moi simplement (ou choisis) :\n\n- 🧠 **Révision active** : on teste tes connaissances avec des questions rapides sur tes points à consolider.\n- ✏️ **Exercice guidé** : on prend un exercice type, je t'accompagne pas à pas.\n- 📘 **Fiche de synthèse** : je te prépare un mémo clair et structuré sur une notion précise.\n\nTu veux qu'on parte sur quoi ? Et si tu as un sujet précis en tête (ex : fractions, fonctions, conjugaison, etc.), je m'adapte immédiatement. 😊	0	2026-09-01 09:04:52.814944+00	f	f	\N	\N	\N	\N	2947
\.


--
-- Data for Name: Notifications; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Notifications" ("Id", "UserId", "Title", "Message", "Type", "RelatedEntityType", "RelatedEntityId", "IsRead", "CreatedAt", "ReadAt") FROM stdin;
5	2	Inscription confirmée	Vous êtes inscrite au cours Français Dissertation.	enrollment	Subject	3	t	2025-12-18 16:32:00+00	\N
6	2	Promotion spéciale	Profitez de -20% sur tous les cours avec le code FEVRIER2026.	promo	\N	\N	f	2026-02-15 09:00:00+00	\N
7	4	Bienvenue sur WinPlus !	Votre compte a été créé avec succès. Explorez nos cours !	welcome	\N	\N	t	2026-01-05 13:00:00+00	\N
8	4	Avis publié	Merci pour votre avis sur Anglais Conversationnel.	review	Subject	5	t	2025-12-30 13:10:00+00	\N
9	5	Rapport hebdomadaire	Le rapport de progression de votre enfant est disponible.	report	\N	\N	f	2026-02-17 08:00:00+00	\N
10	9	Rapport hebdomadaire	Le rapport de progression de votre enfant est disponible.	report	\N	\N	f	2026-02-17 08:00:00+00	\N
11	12	Nouveau paiement	Votre paiement de 49.99 XAF pour React & Next.js a été confirmé.	payment	Order	13	t	2026-02-01 10:32:00+00	\N
\.


--
-- Data for Name: OAuthAccounts; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."OAuthAccounts" ("Id", "UserId", "Provider", "ProviderUserId", "DisplayName", "ProfileImageUrl", "Email", "ConnectedAt", "DisconnectedAt") FROM stdin;
\.


--
-- Data for Name: OrderItems; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."OrderItems" ("Id", "OrderId", "SubjectId", "PriceAtPurchase", "CourseId") FROM stdin;
1	18	30	39.99	\N
2	19	30	39.99	\N
3	20	30	39.99	\N
4	20	29	22.99	\N
5	21	30	39.99	\N
6	21	29	22.99	\N
7	22	30	39.99	\N
8	22	29	22.99	\N
9	23	30	39.99	\N
10	23	29	22.99	\N
11	24	30	39.99	\N
12	24	29	22.99	\N
13	25	30	39.99	\N
14	25	29	22.99	\N
15	26	30	39.99	\N
16	26	29	22.99	\N
17	27	30	39.99	\N
18	27	29	22.99	\N
19	28	30	39.99	\N
20	28	29	22.99	\N
21	29	30	39.99	\N
22	29	29	22.99	\N
23	30	30	39.99	\N
24	30	29	22.99	\N
25	31	30	39.99	\N
26	31	29	22.99	\N
27	32	30	39.99	\N
28	32	29	22.99	\N
29	33	28	19.99	\N
30	33	29	22.99	\N
\.


--
-- Data for Name: Orders; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Orders" ("Id", "UserId", "OrderNumber", "TotalAmount", "Status", "PaymentMethod", "TransactionId", "OrderDate", "CompletedDate", "Notes", "GuestEmail", "GuestName", "DiscountAmount", "IsDeleted", "CreatedAt", "UpdatedAt") FROM stdin;
4	2	WP-2026-0004	29.99	completed	mobile_money	TXN-MTN-003	2025-12-15 11:00:00+00	2025-12-15 11:01:00+00	Mathématiques Terminale S	\N	\N	0.00	f	2025-12-15 11:00:00+00	2025-12-15 11:01:00+00
5	2	WP-2026-0005	19.99	completed	card	TXN-CARD-001	2025-12-18 16:30:00+00	2025-12-18 16:31:00+00	Français Dissertation	\N	\N	0.00	f	2025-12-18 16:30:00+00	2025-12-18 16:31:00+00
6	2	WP-2026-0006	34.99	completed	mobile_money	TXN-MTN-004	2025-12-20 08:45:00+00	2025-12-20 08:46:00+00	Anglais Conversationnel	\N	\N	0.00	f	2025-12-20 08:45:00+00	2025-12-20 08:46:00+00
7	4	WP-2026-0007	34.99	completed	orange_money	TXN-OM-002	2026-01-05 13:20:00+00	2026-01-05 13:21:00+00	Anglais Conversationnel	\N	\N	0.00	f	2026-01-05 13:20:00+00	2026-01-05 13:21:00+00
8	4	WP-2026-0008	29.99	completed	mobile_money	TXN-MTN-005	2026-01-08 10:00:00+00	2026-01-08 10:01:00+00	Marketing Digital	\N	\N	0.00	f	2026-01-08 10:00:00+00	2026-01-08 10:01:00+00
9	5	WP-2026-0009	39.99	completed	card	TXN-CARD-002	2026-01-15 15:30:00+00	2026-01-15 15:31:00+00	Informatique Python	\N	\N	0.00	f	2026-01-15 15:30:00+00	2026-01-15 15:31:00+00
10	5	WP-2026-0010	49.99	completed	mobile_money	TXN-MTN-006	2026-01-18 09:00:00+00	2026-01-18 09:01:00+00	React & Next.js	\N	\N	0.00	f	2026-01-18 09:00:00+00	2026-01-18 09:01:00+00
11	9	WP-2026-0011	34.99	completed	orange_money	TXN-OM-003	2026-01-22 11:00:00+00	2026-01-22 11:01:00+00	Anglais Conversationnel	\N	\N	0.00	f	2026-01-22 11:00:00+00	2026-01-22 11:01:00+00
12	9	WP-2026-0012	39.99	completed	mobile_money	TXN-MTN-007	2026-01-25 14:15:00+00	2026-01-25 14:16:00+00	Informatique Python	\N	\N	0.00	f	2026-01-25 14:15:00+00	2026-01-25 14:16:00+00
13	12	WP-2026-0013	49.99	completed	card	TXN-CARD-003	2026-02-01 10:30:00+00	2026-02-01 10:31:00+00	React & Next.js	\N	\N	0.00	f	2026-02-01 10:30:00+00	2026-02-01 10:31:00+00
14	13	WP-2026-0014	39.99	completed	mobile_money	TXN-MTN-008	2026-02-05 08:45:00+00	2026-02-05 08:46:00+00	Informatique Python	\N	\N	0.00	f	2026-02-05 08:45:00+00	2026-02-05 08:46:00+00
15	13	WP-2026-0015	44.99	completed	orange_money	TXN-OM-004	2026-02-10 16:00:00+00	2026-02-10 16:01:00+00	JavaScript Avancé	\N	\N	0.00	f	2026-02-10 16:00:00+00	2026-02-10 16:01:00+00
16	6	WP-2026-0016	19.99	pending	mobile_money	\N	2026-02-17 12:00:00+00	\N	Espagnol Débutant	\N	\N	0.00	f	2026-02-17 12:00:00+00	2026-02-17 12:00:00+00
17	8	WP-2026-0017	34.99	pending	orange_money	\N	2026-02-18 09:30:00+00	\N	Biologie Humaine	\N	\N	0.00	f	2026-02-18 09:30:00+00	2026-02-18 09:30:00+00
18	\N	ORD-20260807040623-7429	39.99	pending	mtn	\N	2026-08-07 04:06:23.847858+00	\N	\N	\N	\N	0.00	f	2026-08-07 04:06:23.847859+00	2026-08-07 04:06:23.847858+00
19	\N	ORD-20260807040800-4891	39.99	pending	orange	\N	2026-08-07 04:08:00.449874+00	\N	\N	\N	\N	0.00	f	2026-08-07 04:08:00.449874+00	2026-08-07 04:08:00.449874+00
20	\N	ORD-20260807212205-9333	62.98	pending	orange	\N	2026-08-07 21:22:05.382772+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-07 21:22:05.382773+00	2026-08-07 21:22:05.382772+00
21	\N	ORD-20260807214441-6841	62.98	pending	orange	\N	2026-08-07 21:44:41.282906+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-07 21:44:41.282906+00	2026-08-07 21:44:41.282906+00
22	\N	ORD-20260807221100-5378	62.98	pending	orange	\N	2026-08-07 22:11:00.102041+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-07 22:11:00.102041+00	2026-08-07 22:11:00.102041+00
23	\N	ORD-20260807223647-3604	62.98	pending	orange	\N	2026-08-07 22:36:47.545604+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-07 22:36:47.545604+00	2026-08-07 22:36:47.545604+00
24	\N	ORD-20260807225440-7460	62.98	pending	orange	\N	2026-08-07 22:54:40.634477+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-07 22:54:40.634477+00	2026-08-07 22:54:40.634477+00
25	\N	ORD-20260807225531-2508	62.98	pending	orange	\N	2026-08-07 22:55:31.938678+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-07 22:55:31.938678+00	2026-08-07 22:55:31.938678+00
26	\N	ORD-20260807232702-2733	62.98	pending	orange	\N	2026-08-07 23:27:02.509863+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-07 23:27:02.509863+00	2026-08-07 23:27:02.509863+00
27	\N	ORD-20260807234314-2847	62.98	pending	orange	\N	2026-08-07 23:43:14.742511+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-07 23:43:14.742511+00	2026-08-07 23:43:14.742511+00
28	\N	ORD-20260807235344-4077	62.98	pending	orange	\N	2026-08-07 23:53:44.385717+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-07 23:53:44.385717+00	2026-08-07 23:53:44.385717+00
29	\N	ORD-20260808000706-4695	62.98	pending	orange	\N	2026-08-08 00:07:06.401045+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-08 00:07:06.401046+00	2026-08-08 00:07:06.401045+00
30	\N	ORD-20260808001602-2786	62.98	pending	orange	\N	2026-08-08 00:16:02.139857+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-08 00:16:02.139857+00	2026-08-08 00:16:02.139857+00
31	\N	ORD-20260808002914-6280	62.98	pending	orange	\N	2026-08-08 00:29:14.238701+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-08 00:29:14.238701+00	2026-08-08 00:29:14.238701+00
32	\N	ORD-20260808031328-7711	62.98	pending	orange	\N	2026-08-08 03:13:28.827945+00	\N	\N	mko.digital.237@gmail.com	\N	0.00	f	2026-08-08 03:13:28.827945+00	2026-08-08 03:13:28.827945+00
33	\N	ORD-20260820151738-7223	42.98	pending	mtn	\N	2026-08-20 15:17:38.569983+00	\N	\N	nkamfangpaolo@gmail.com	\N	0.00	f	2026-08-20 15:17:38.569983+00	2026-08-20 15:17:38.569983+00
\.


--
-- Data for Name: Pages; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Pages" ("Id", "Slug", "Title", "Content", "MetaDescription", "MetaKeywords", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy", "Order") FROM stdin;
1	about	À propos de WinPlus	<h2>Notre Mission</h2>\n<p>WinPlus est une plateforme éducative camerounaise dédiée à la réussite scolaire et académique. Fondée en 2025, notre mission est de rendre l'éducation de qualité accessible à tous les étudiants du Cameroun et d'Afrique francophone.</p>\n\n<h2>Ce que nous offrons</h2>\n<p>Notre plateforme propose des cours interactifs, des épreuves d'examens officiels (BAC, Probatoire, BEPC, Concours), des quiz adaptatifs générés par intelligence artificielle, et des fiches de révision complètes couvrant toutes les matières du programme camerounais.</p>\n\n<h2>Notre Équipe</h2>\n<p>WinPlus est portée par une équipe passionnée d'enseignants expérimentés et de développeurs talentueux, tous engagés pour l'excellence éducative. Nos professeurs sont des experts dans leurs domaines respectifs, avec des années d'expérience dans l'enseignement au Cameroun.</p>\n\n<h2>Notre Vision</h2>\n<p>Devenir la référence en matière d'éducation numérique en Afrique centrale, en combinant technologie de pointe et pédagogie adaptée au contexte local.</p>\n\n<h2>Contactez-nous</h2>\n<p>Email : contact@winplus.cm<br>Téléphone : +237 6XX XXX XXX<br>Adresse : Yaoundé, Cameroun</p>	WinPlus - Plateforme éducative camerounaise pour la réussite scolaire. Cours, épreuves BAC, Probatoire, BEPC et concours.	éducation, cameroun, bac, probatoire, bepc, concours, cours en ligne, winplus	t	2026-01-15 08:00:00+00	2026-02-20 15:00:15.141262+00	2026-02-20 15:00:15.141262+00	\N	\N	0
2	terms	Conditions d'utilisation	<h2>1. Acceptation des conditions</h2>\n<p>En accédant et en utilisant la plateforme WinPlus (winplus.cm), vous acceptez d'être lié par les présentes conditions d'utilisation. Si vous n'acceptez pas ces conditions, veuillez ne pas utiliser notre service.</p>\n\n<h2>2. Description du service</h2>\n<p>WinPlus est une plateforme éducative en ligne proposant des cours, des épreuves d'examens, des quiz et des fiches de révision. Le service est accessible via navigateur web et applications mobiles.</p>\n\n<h2>3. Inscription et compte</h2>\n<p>Pour accéder à certaines fonctionnalités, vous devez créer un compte. Vous êtes responsable de la confidentialité de vos identifiants de connexion. Vous devez fournir des informations exactes et à jour lors de l'inscription.</p>\n\n<h2>4. Contenu et propriété intellectuelle</h2>\n<p>Tout le contenu disponible sur WinPlus (cours, vidéos, documents, quiz) est protégé par le droit d'auteur. Vous bénéficiez d'une licence personnelle, non transférable et non exclusive pour accéder au contenu dans le cadre de votre apprentissage.</p>\n\n<h2>5. Abonnements et paiements</h2>\n<p>Certains contenus sont gratuits, d'autres nécessitent un abonnement payant. Les prix sont affichés en FCFA. Les paiements sont acceptés par Mobile Money (MTN, Orange) et carte bancaire. Les abonnements se renouvellent automatiquement sauf annulation.</p>\n\n<h2>6. Politique de remboursement</h2>\n<p>Les demandes de remboursement peuvent être effectuées dans les 7 jours suivant l'achat si le contenu n'a pas été consulté à plus de 20%. Au-delà, aucun remboursement ne sera accordé.</p>\n\n<h2>7. Comportement des utilisateurs</h2>\n<p>Les utilisateurs s'engagent à ne pas partager leur compte, ne pas redistribuer le contenu, ne pas perturber le fonctionnement de la plateforme, et à respecter les autres utilisateurs dans les espaces communautaires.</p>\n\n<h2>8. Limitation de responsabilité</h2>\n<p>WinPlus s'efforce de fournir un contenu de qualité mais ne garantit pas les résultats aux examens. La plateforme est fournie "en l'état" et nous ne pouvons être tenus responsables des interruptions de service.</p>\n\n<h2>9. Modification des conditions</h2>\n<p>WinPlus se réserve le droit de modifier ces conditions à tout moment. Les utilisateurs seront notifiés des changements importants par email.</p>\n\n<h2>10. Droit applicable</h2>\n<p>Les présentes conditions sont régies par le droit camerounais. Tout litige sera soumis aux tribunaux compétents de Yaoundé.</p>\n\n<p><em>Dernière mise à jour : 15 janvier 2026</em></p>	Conditions d'utilisation de la plateforme éducative WinPlus.	conditions, utilisation, termes, winplus, légal	t	2026-01-15 08:00:00+00	2026-02-20 15:00:15.141262+00	2026-02-20 15:00:15.141262+00	\N	\N	0
3	privacy	Politique de confidentialité	<h2>1. Collecte des données</h2>\n<p>WinPlus collecte les données suivantes lors de votre utilisation : informations d'inscription (nom, email, rôle), données de progression (cours suivis, scores, temps passé), données de paiement (traitées par nos partenaires sécurisés), et données techniques (adresse IP, navigateur, appareil).</p>\n\n<h2>2. Utilisation des données</h2>\n<p>Vos données sont utilisées pour : fournir et améliorer nos services, personnaliser votre expérience d'apprentissage, générer des recommandations adaptées via notre IA, communiquer avec vous (notifications, newsletters si vous y avez consenti), et établir des statistiques anonymisées.</p>\n\n<h2>3. Protection des données</h2>\n<p>Nous mettons en œuvre des mesures de sécurité techniques et organisationnelles pour protéger vos données : chiffrement SSL/TLS, hashage des mots de passe, accès restreint aux données personnelles, sauvegardes régulières sécurisées.</p>\n\n<h2>4. Partage des données</h2>\n<p>Vos données ne sont jamais vendues à des tiers. Elles peuvent être partagées avec : nos prestataires de paiement (MTN MoMo, Orange Money), nos hébergeurs (dans le respect du RGPD), et les autorités si requis par la loi.</p>\n\n<h2>5. Données des mineurs</h2>\n<p>Les utilisateurs de moins de 18 ans doivent avoir l'autorisation de leurs parents ou tuteurs légaux. Les comptes "Parent" permettent de superviser l'activité des enfants sur la plateforme.</p>\n\n<h2>6. Vos droits</h2>\n<p>Conformément à la loi camerounaise et aux bonnes pratiques internationales, vous avez le droit de : accéder à vos données personnelles, rectifier vos informations, supprimer votre compte et données, exporter vos données, et retirer votre consentement aux communications marketing.</p>\n\n<h2>7. Cookies</h2>\n<p>WinPlus utilise des cookies essentiels au fonctionnement du site et des cookies analytiques (anonymisés) pour améliorer nos services. Vous pouvez gérer vos préférences de cookies dans les paramètres de votre navigateur.</p>\n\n<h2>8. Contact</h2>\n<p>Pour toute question relative à vos données personnelles : privacy@winplus.cm</p>\n\n<p><em>Dernière mise à jour : 15 janvier 2026</em></p>	Politique de confidentialité WinPlus. Comment nous protégeons vos données personnelles.	confidentialité, données personnelles, vie privée, RGPD, winplus	t	2026-01-15 08:00:00+00	2026-02-20 15:00:15.141262+00	2026-02-20 15:00:15.141262+00	\N	\N	0
4	faq	Questions Fréquentes	<h2>Général</h2>\n\n<h3>Qu'est-ce que WinPlus ?</h3>\n<p>WinPlus est une plateforme éducative en ligne camerounaise qui propose des cours, des épreuves d'examens officiels, des quiz interactifs et des fiches de révision pour les étudiants de tous niveaux.</p>\n\n<h3>WinPlus est-il gratuit ?</h3>\n<p>WinPlus propose des contenus gratuits et des contenus premium. Vous pouvez accéder à certains cours et épreuves gratuitement. Pour un accès illimité, consultez nos plans d'abonnement.</p>\n\n<h2>Compte et inscription</h2>\n\n<h3>Comment créer un compte ?</h3>\n<p>Cliquez sur "S'inscrire", remplissez le formulaire avec votre email et choisissez votre rôle (étudiant, enseignant ou parent). Vous pouvez aussi vous inscrire via Google.</p>\n\n<h3>J'ai oublié mon mot de passe</h3>\n<p>Cliquez sur "Mot de passe oublié" sur la page de connexion. Un lien de réinitialisation vous sera envoyé par email.</p>\n\n<h2>Paiements</h2>\n\n<h3>Quels moyens de paiement acceptez-vous ?</h3>\n<p>Nous acceptons MTN Mobile Money, Orange Money et les cartes bancaires (Visa, Mastercard).</p>\n\n<h3>Puis-je me faire rembourser ?</h3>\n<p>Oui, dans les 7 jours suivant l'achat si vous n'avez pas consulté plus de 20% du contenu.</p>\n\n<h2>Épreuves et examens</h2>\n\n<h3>Quelles épreuves sont disponibles ?</h3>\n<p>Nous proposons des épreuves du BAC (séries A, C, D), du Probatoire, du BEPC, et des concours d'entrée aux grandes écoles (ENS, Polytechnique, ENAM, FMSB, ESSEC, etc.).</p>\n\n<h3>Les corrigés sont-ils inclus ?</h3>\n<p>La majorité des épreuves sont accompagnées de corrigés détaillés. Les épreuves sans corrigé sont clairement indiquées.</p>	FAQ WinPlus - Réponses à vos questions sur notre plateforme éducative.	faq, questions, aide, support, winplus	t	2026-01-20 08:00:00+00	2026-02-20 15:00:15.141262+00	2026-02-20 15:00:15.141262+00	\N	\N	0
5	contact	Nous Contacter	<h2>Contactez l'équipe WinPlus</h2>\n\n<p>Nous sommes à votre écoute pour toute question, suggestion ou partenariat.</p>\n\n<h3>Email</h3>\n<p>Support : support@winplus.cm<br>\nPartenariats : partenaires@winplus.cm<br>\nPresse : presse@winplus.cm</p>\n\n<h3>Téléphone</h3>\n<p>+237 6XX XXX XXX (Lundi - Vendredi, 8h - 18h)</p>\n\n<h3>Adresse</h3>\n<p>WinPlus SARL<br>Quartier Bastos<br>Yaoundé, Cameroun</p>\n\n<h3>Réseaux sociaux</h3>\n<p>Facebook : @WinPlusCM<br>\nTwitter/X : @WinPlusCM<br>\nInstagram : @winplus.cm<br>\nLinkedIn : WinPlus Cameroun</p>	Contactez WinPlus - Support, partenariats et informations.	contact, support, aide, winplus, cameroun	t	2026-01-20 08:00:00+00	2026-02-20 15:00:15.141262+00	2026-02-20 15:00:15.141262+00	\N	\N	0
\.


--
-- Data for Name: ParentCreditLedgers; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ParentCreditLedgers" ("Id", "ParentId", "EntryType", "Amount", "ChildId", "OrderId", "Label", "PeriodStart", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: ParentStudentLinks; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."ParentStudentLinks" ("Id", "ParentId", "StudentId", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: PasswordResetTokens; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PasswordResetTokens" ("Id", "UserId", "Token", "ExpiresAt", "IsUsed", "UsedAt", "CreatedAt") FROM stdin;
1	17	2qpptCNSesgunElctPqSkgsW5wlmY5I80M+MdhpM/8vE5Rjc197Mv0jeovEsZkhTB9aQWvQBHEpveaD5Pfh9iQ==	2026-06-16 14:45:06.516433+00	f	\N	2026-06-16 13:45:06.516468+00
9	26	+4PK4S1RvoZrNQqw6gypse4fppd1uVPVESm/+bWf7DC9Z+p2scPTo2t86aURfQoPkjRDLakrWtDTGoGmDU74wg==	2026-08-30 21:05:35.865938+00	f	\N	2026-08-30 20:05:35.865972+00
11	37	0+9ixxpSZhj3ra4RFGsbS8uLrqaQ6i0n+lOVi+UCwJbY+pKBBA84bXEqY2pQjwCalyBGxqvb9QwbvIP5azSOwA==	2026-08-31 12:12:30.452709+00	t	2026-08-31 11:16:56.475423+00	2026-08-31 11:12:30.452741+00
12	37	L2gpdJty31So866V+p5aPL6PO8oHnhUde1RUd8bmqB8yotwUf2Tbzz3raLBipyRx0w5XR1jgHUnq6I51Yck5Sw==	2026-08-31 12:33:45.190247+00	t	2026-08-31 11:34:54.473523+00	2026-08-31 11:33:45.190289+00
14	39	0o2nmEW0NHWIiOKPjLoQKAmMkVxb3XdUf/eXgM4bMzT56rJUBk0oFQlR43OOlNO77qzRVsHQCGJeFdmgZw22CQ==	2026-09-01 10:12:26.102366+00	t	2026-09-01 09:15:06.673598+00	2026-09-01 09:12:26.102409+00
\.


--
-- Data for Name: Payments; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Payments" ("Id", "OrderId", "UserId", "Amount", "Currency", "Status", "PaymentMethod", "TransactionId", "Description", "FeeAmount", "InitiatedAt", "ProcessedAt", "CompletedAt", "ErrorMessage", "RetryCount", "NextRetryAt", "Metadata", "CreatedAt", "UpdatedAt", "NotchpayReference", "PhoneNumber", "Operator", "ErrorCode", "ExpiresAt", "GuestEmail") FROM stdin;
6	4	2	29.99	XAF	completed	mobile_money	TXN-MTN-003	\N	\N	2025-12-15 11:00:00+00	2025-12-15 11:00:30+00	2025-12-15 11:01:00+00	\N	\N	\N	\N	2025-12-15 11:01:00+00	2025-12-15 11:01:00+00	\N	\N	\N	\N	\N	\N
7	5	2	19.99	XAF	completed	card	TXN-CARD-001	\N	\N	2025-12-18 16:30:00+00	2025-12-18 16:30:30+00	2025-12-18 16:31:00+00	\N	\N	\N	\N	2025-12-18 16:31:00+00	2025-12-18 16:31:00+00	\N	\N	\N	\N	\N	\N
8	6	2	34.99	XAF	completed	mobile_money	TXN-MTN-004	\N	\N	2025-12-20 08:45:00+00	2025-12-20 08:45:30+00	2025-12-20 08:46:00+00	\N	\N	\N	\N	2025-12-20 08:46:00+00	2025-12-20 08:46:00+00	\N	\N	\N	\N	\N	\N
9	7	4	34.99	XAF	completed	orange_money	TXN-OM-002	\N	\N	2026-01-05 13:20:00+00	2026-01-05 13:20:30+00	2026-01-05 13:21:00+00	\N	\N	\N	\N	2026-01-05 13:21:00+00	2026-01-05 13:21:00+00	\N	\N	\N	\N	\N	\N
10	8	4	29.99	XAF	completed	mobile_money	TXN-MTN-005	\N	\N	2026-01-08 10:00:00+00	2026-01-08 10:00:30+00	2026-01-08 10:01:00+00	\N	\N	\N	\N	2026-01-08 10:01:00+00	2026-01-08 10:01:00+00	\N	\N	\N	\N	\N	\N
11	9	5	39.99	XAF	completed	card	TXN-CARD-002	\N	\N	2026-01-15 15:30:00+00	2026-01-15 15:30:30+00	2026-01-15 15:31:00+00	\N	\N	\N	\N	2026-01-15 15:31:00+00	2026-01-15 15:31:00+00	\N	\N	\N	\N	\N	\N
12	10	5	49.99	XAF	completed	mobile_money	TXN-MTN-006	\N	\N	2026-01-18 09:00:00+00	2026-01-18 09:00:30+00	2026-01-18 09:01:00+00	\N	\N	\N	\N	2026-01-18 09:01:00+00	2026-01-18 09:01:00+00	\N	\N	\N	\N	\N	\N
13	11	9	34.99	XAF	completed	orange_money	TXN-OM-003	\N	\N	2026-01-22 11:00:00+00	2026-01-22 11:00:30+00	2026-01-22 11:01:00+00	\N	\N	\N	\N	2026-01-22 11:01:00+00	2026-01-22 11:01:00+00	\N	\N	\N	\N	\N	\N
14	12	9	39.99	XAF	completed	mobile_money	TXN-MTN-007	\N	\N	2026-01-25 14:15:00+00	2026-01-25 14:15:30+00	2026-01-25 14:16:00+00	\N	\N	\N	\N	2026-01-25 14:16:00+00	2026-01-25 14:16:00+00	\N	\N	\N	\N	\N	\N
15	13	12	49.99	XAF	completed	card	TXN-CARD-003	\N	\N	2026-02-01 10:30:00+00	2026-02-01 10:30:30+00	2026-02-01 10:31:00+00	\N	\N	\N	\N	2026-02-01 10:31:00+00	2026-02-01 10:31:00+00	\N	\N	\N	\N	\N	\N
16	14	13	39.99	XAF	completed	mobile_money	TXN-MTN-008	\N	\N	2026-02-05 08:45:00+00	2026-02-05 08:45:30+00	2026-02-05 08:46:00+00	\N	\N	\N	\N	2026-02-05 08:46:00+00	2026-02-05 08:46:00+00	\N	\N	\N	\N	\N	\N
17	15	13	44.99	XAF	completed	orange_money	TXN-OM-004	\N	\N	2026-02-10 16:00:00+00	2026-02-10 16:00:30+00	2026-02-10 16:01:00+00	\N	\N	\N	\N	2026-02-10 16:01:00+00	2026-02-10 16:01:00+00	\N	\N	\N	\N	\N	\N
18	23	\N	74.98	XAF	failed	notchpay	\N	Paiement commande #23	\N	2026-08-07 22:36:48.589683+00	\N	\N	NotchPay error: Unauthorized	0	\N	\N	2026-08-07 22:36:48.589542+00	2026-08-07 22:36:56.746589+00	\N	237691697924	\N	\N	2026-08-07 23:36:48.589697+00	mko.digital.237@gmail.com
21	26	\N	74.98	XAF	failed	notchpay	\N	Paiement commande #26	\N	2026-08-07 23:27:05.534253+00	\N	\N	\N	0	\N	\N	2026-08-07 23:27:05.534005+00	2026-08-08 00:43:08.119501+00	trx.UWpS7XE46DbPXYyVD86SIcLF	237691697924	\N	\N	2026-08-08 00:27:05.534273+00	mko.digital.237@gmail.com
19	24	\N	74.98	XAF	expired	notchpay	\N	Paiement commande #24	\N	2026-08-07 22:54:43.877125+00	\N	\N	Paiement expiré après 1 heure sans confirmation	0	\N	\N	2026-08-07 22:54:43.876974+00	2026-08-08 01:28:07.011705+00	trx.URQQt4JWBOJN7gKt7ve18ocw	237691697924	\N	\N	2026-08-07 23:54:43.87714+00	mko.digital.237@gmail.com
20	25	\N	74.98	XAF	expired	notchpay	\N	Paiement commande #25	\N	2026-08-07 22:55:32.564458+00	\N	\N	Paiement expiré après 1 heure sans confirmation	0	\N	\N	2026-08-07 22:55:32.564448+00	2026-08-08 01:28:07.014646+00	trx.yJjwKWAZDx079NKiZuIgkEe0	237691697924	\N	\N	2026-08-07 23:55:32.564459+00	mko.digital.237@gmail.com
22	27	\N	74.98	XAF	expired	notchpay	\N	Paiement commande #27	\N	2026-08-07 23:43:15.804388+00	\N	\N	Paiement expiré après 1 heure sans confirmation	0	\N	\N	2026-08-07 23:43:15.804219+00	2026-08-08 01:28:07.017468+00	trx.ZkHMQyM9qvnrjoZwOq2BMHd6	237691697924	\N	\N	2026-08-08 00:43:15.804402+00	mko.digital.237@gmail.com
23	28	\N	74.98	XAF	expired	notchpay	\N	Paiement commande #28	\N	2026-08-07 23:53:47.60875+00	\N	\N	Paiement expiré après 1 heure sans confirmation	0	\N	\N	2026-08-07 23:53:47.608549+00	2026-08-08 01:28:07.019525+00	trx.m1Yu0b61uQelE5lAGPZDuf8x	237691697924	\N	\N	2026-08-08 00:53:47.608766+00	mko.digital.237@gmail.com
24	29	\N	74.98	XAF	expired	notchpay	\N	Paiement commande #29	\N	2026-08-08 00:07:09.602825+00	\N	\N	Paiement expiré après 1 heure sans confirmation	0	\N	\N	2026-08-08 00:07:09.602676+00	2026-08-08 01:28:07.02164+00	trx.xwIGH07LqpSCcPgvrMEHKkAk	237691697924	\N	\N	2026-08-08 01:07:09.60284+00	mko.digital.237@gmail.com
25	30	\N	74.98	XAF	expired	notchpay	\N	Paiement commande #30	\N	2026-08-08 00:16:05.130759+00	\N	\N	Paiement expiré après 1 heure sans confirmation	0	\N	\N	2026-08-08 00:16:05.130577+00	2026-08-08 01:28:07.023838+00	trx.lY8eyEjNIBNdG4vrhmGdQvQN	237691697924	\N	\N	2026-08-08 01:16:05.130787+00	mko.digital.237@gmail.com
26	31	\N	74.98	XAF	expired	notchpay	\N	Paiement commande #31	\N	2026-08-08 00:29:17.27945+00	\N	\N	Paiement expiré après 1 heure sans confirmation	0	\N	\N	2026-08-08 00:29:17.279283+00	2026-08-08 02:28:07.195795+00	trx.B5Vvpu18YaJNLXcIMYkk1ozm	237691697924	\N	\N	2026-08-08 01:29:17.279467+00	mko.digital.237@gmail.com
27	32	\N	74.98	XAF	failed	notchpay	\N	Paiement commande #32	\N	2026-08-08 03:13:29.917627+00	\N	\N	\N	0	\N	\N	2026-08-08 03:13:29.917489+00	2026-08-08 03:33:01.021323+00	trx.5JuGCHvPb0JEb3cv2AK8FPpe	237691697924	\N	\N	2026-08-08 04:13:29.917641+00	mko.digital.237@gmail.com
28	33	\N	50.98	XAF	failed	notchpay	\N	WinPlus  Épreuves scolaires	\N	2026-08-20 15:17:39.097736+00	\N	\N	NotchPay rejected request: Unauthorized  {"code":"401","status":"Unauthorized","message":"Invalid API credentials"}	0	\N	\N	2026-08-20 15:17:39.097598+00	2026-08-20 15:17:39.780948+00	\N	237653800185	\N	\N	2026-08-20 16:17:39.09775+00	nkamfangpaolo@gmail.com
\.


--
-- Data for Name: PlatformAnomalies; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PlatformAnomalies" ("Id", "Type", "Severity", "Description", "DetectedAt", "ResolvedAt", "Resolution", "Metadata") FROM stdin;
\.


--
-- Data for Name: PricingPlans; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PricingPlans" ("Id", "Name", "Category", "Price", "Period", "Features", "IsPopular", "Icon", "Description", "CreatedAt", "UpdatedAt", "IsDeleted", "IsArchived", "Currency", "BillingPeriod", "MaxDownloads", "MaxChatMessages", "MonthlyCredits", "TeacherRevenueShare", "MaxChildren") FROM stdin;
1	Starter	students	0.00	/mois	["Accès 3 cours gratuits","Support communauté","Certificats basiques"]	f	\N	Parfait pour commencer	2026-02-18 13:09:02.595677+00	\N	f	f	XAF	\N	\N	\N	\N	\N	\N
2	Standard	students	5900.00	/mois	["Accès 20 cours","Support email","Certificats officiels","Quiz illimités"]	f	\N	Pour les étudiants réguliers	2026-02-18 13:09:02.595677+00	\N	f	f	XAF	\N	\N	\N	\N	\N	\N
3	Premium	students	11900.00	/mois	["Accès illimité","Support prioritaire","Certificats officiels","Quiz illimités","Sessions live","Mentor dédié"]	t	\N	Le meilleur pour réussir	2026-02-18 13:09:02.595677+00	\N	f	f	XAF	\N	\N	\N	\N	\N	\N
4	Annuel	students	89900.00	/an	["Tout Premium","2 mois offerts","Accès offline","Ressources exclusives"]	f	\N	Économisez 38% par an	2026-02-18 13:09:02.595677+00	\N	f	f	XAF	\N	\N	\N	\N	\N	\N
5	Basique	teachers	0.00	/mois	["Créer 2 cours","50 étudiants max","Analytics basiques"]	f	\N	Pour débuter l'enseignement	2026-02-18 13:09:02.595677+00	\N	f	f	XAF	\N	\N	\N	\N	\N	\N
6	Pro	teachers	17900.00	/mois	["Cours illimités","500 étudiants","Analytics avancés","Monétisation"]	t	\N	Pour les enseignants actifs	2026-02-18 13:09:02.595677+00	\N	f	f	XAF	\N	\N	\N	\N	\N	\N
7	Expert	teachers	35900.00	/mois	["Tout Pro","Étudiants illimités","Support dédié","Webinaires","Certification WinPlus"]	f	\N	Pour les experts reconnus	2026-02-18 13:09:02.595677+00	\N	f	f	XAF	\N	\N	\N	\N	\N	\N
8	Famille	parents	8900.00	/mois	["2 enfants","Suivi détaillé","Rapports hebdo","Support email"]	f	\N	Idéal pour la famille	2026-02-18 13:09:02.595677+00	\N	f	f	XAF	\N	\N	\N	\N	\N	\N
9	Famille+	parents	14900.00	/mois	["4 enfants","Suivi en temps réel","Rapports quotidiens","Support prioritaire","Réunions virtuelles"]	t	\N	Pour les grandes familles	2026-02-18 13:09:02.595677+00	\N	f	f	XAF	\N	\N	\N	\N	\N	\N
10	VIP	parents	26900.00	/mois	["Enfants illimités","Suivi personnalisé","Coach dédié","Accès tous cours","Bilan mensuel"]	f	\N	Accompagnement premium	2026-02-18 13:09:02.595677+00	\N	f	f	XAF	\N	\N	\N	\N	\N	\N
\.


--
-- Data for Name: PromoCodeUsages; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PromoCodeUsages" ("Id", "PromoCodeId", "UserId", "OrderId", "DiscountAmount", "UsedAt") FROM stdin;
\.


--
-- Data for Name: PromoCodes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PromoCodes" ("Id", "Code", "Description", "DiscountType", "DiscountValue", "MinimumPurchase", "MaximumDiscount", "UsageLimit", "UsageCount", "PerUserLimit", "ValidFrom", "ValidUntil", "IsActive", "ApplicableSubjectIds", "CreatedBy", "CreatedAt", "UpdatedAt") FROM stdin;
1	TEST2026	Code promo de test — 20% de réduction	Percentage	20.00	\N	\N	100	0	1	2026-09-01 22:30:39.667469+00	2027-09-01 22:30:39.667469+00	t	\N	17	2026-09-01 22:30:39.667469+00	\N
\.


--
-- Data for Name: Promotions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Promotions" ("Id", "Code", "DiscountPercent", "ValidFrom", "ValidUntil", "IsActive", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: QuizAttempts; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."QuizAttempts" ("Id", "UserId", "QuizId", "UserAnswersJson", "Score", "CorrectAnswers", "TimeSpentSeconds", "Status", "IsCompleted", "Passed", "AttemptNumber", "StartedAt", "CompletedAt") FROM stdin;
\.


--
-- Data for Name: QuizMistakes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."QuizMistakes" ("Id", "UserId", "QuizId", "QuizAttemptId", "Subject", "Question", "GivenAnswer", "CorrectAnswer", "IsResolved", "ResolvedAt", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: Quizzes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Quizzes" ("Id", "Title", "SubjectId", "CreatedBy", "Difficulty", "QuestionCount", "TimeLimit", "PassingScore", "Questions", "IsPublished", "AttemptCount", "AverageScore", "CreatedAt", "UpdatedAt", "IsDeleted", "Description", "QuestionsJson", "ExamId", "IsAIGenerated", "Attempts", "PassingAttempts", "TotalScore", "Tags", "PublishedAt") FROM stdin;
1	Quiz - Limites et Continuité	1	3	medium	10	30	60	[{"id": "q1", "options": ["0", "1", "2", "∞"], "question": "Quelle est la limite de (x²-1)/(x-1) quand x tend vers 1 ?", "explanation": "On factorise: (x-1)(x+1)/(x-1) = x+1, donc la limite est 2.", "correctAnswer": "2"}, {"id": "q2", "options": ["Oui, toujours", "Non, jamais", "Seulement si f est dérivable", "Seulement si [a,b] est fermé borné"], "question": "Une fonction continue sur [a,b] atteint-elle toujours ses bornes ?", "explanation": "Théorème des bornes atteintes pour une fonction continue sur un fermé borné.", "correctAnswer": "Oui, toujours"}, {"id": "q3", "options": ["1/x", "x", "ln(x)/x", "1/x²"], "question": "Quelle est la dérivée de ln(x) ?", "explanation": "La dérivée de ln(x) est 1/x pour x > 0.", "correctAnswer": "1/x"}, {"id": "q4", "options": ["Oui", "Non", "Seulement à droite", "Indéterminé"], "question": "La fonction f(x)=|x| est-elle dérivable en 0 ?", "explanation": "La fonction valeur absolue a un point anguleux en 0.", "correctAnswer": "Non"}, {"id": "q5", "options": ["0", "1", "∞", "N'existe pas"], "question": "Quelle est la limite de sin(x)/x quand x→0 ?", "explanation": "C'est une limite classique fondamentale.", "correctAnswer": "1"}, {"id": "q6", "options": ["Un maximum", "Un minimum", "Un point critique", "Un point d'inflexion"], "question": "Si f est dérivable et f'(a)=0, alors a est:", "explanation": "f'(a)=0 signifie que a est un point critique, pas forcément un extremum.", "correctAnswer": "Un point critique"}, {"id": "q7", "options": ["Continue", "Discontinue", "Dérivable", "Intégrable"], "question": "La composée de deux fonctions continues est:", "explanation": "La composée de fonctions continues est continue.", "correctAnswer": "Continue"}, {"id": "q8", "options": ["sin(x)+C", "-sin(x)+C", "cos(x)+C", "tan(x)+C"], "question": "Quelle est la primitive de cos(x) ?", "explanation": "La dérivée de sin(x) est cos(x), donc la primitive de cos(x) est sin(x)+C.", "correctAnswer": "sin(x)+C"}, {"id": "q9", "options": ["0", "1", "+∞", "Dépend de n"], "question": "lim(x→+∞) e^x/x^n = ?", "explanation": "L'exponentielle domine toujours les polynômes à l'infini.", "correctAnswer": "+∞"}, {"id": "q10", "options": ["Oui", "Non", "Seulement si bornée", "Seulement sur un ouvert"], "question": "Une fonction dérivable est-elle forcément continue ?", "explanation": "La dérivabilité implique la continuité.", "correctAnswer": "Oui"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f	\N	[]	\N	f	0	0	0.00	\N	\N
2	Quiz - Lois de Newton	2	3	easy	8	20	60	[{"id": "q1", "options": ["Principe d'inertie", "Principe d'action-réaction", "PFD", "Principe de conservation"], "question": "La première loi de Newton est aussi appelée:", "explanation": "La 1ère loi de Newton est le principe d'inertie.", "correctAnswer": "Principe d'inertie"}, {"id": "q2", "options": ["1ère loi", "2ème loi", "3ème loi", "Loi de gravitation"], "question": "F = ma est la:", "explanation": "Le Principe Fondamental de la Dynamique (PFD) est la 2ème loi.", "correctAnswer": "2ème loi"}, {"id": "q3", "options": ["Joule", "Newton", "Pascal", "Watt"], "question": "L'unité de force dans le SI est:", "explanation": "Le Newton (N) est l'unité de force: 1N = 1kg·m/s².", "correctAnswer": "Newton"}, {"id": "q4", "options": ["Une force extérieure agit", "Il a de la masse", "Il est sur Terre", "Il est en contact avec un autre objet"], "question": "Un objet au repos reste au repos sauf si:", "explanation": "C'est le principe d'inertie (1ère loi de Newton).", "correctAnswer": "Une force extérieure agit"}, {"id": "q5", "options": ["8.9 m/s²", "9.81 m/s²", "10.2 m/s²", "6.67 m/s²"], "question": "L'accélération gravitationnelle sur Terre est environ:", "explanation": "g ≈ 9.81 m/s² à la surface de la Terre.", "correctAnswer": "9.81 m/s²"}, {"id": "q6", "options": ["Double", "Est divisée par 2", "Reste la même", "Quadruple"], "question": "Si la masse double et la force reste constante, l'accélération:", "explanation": "a = F/m, si m double, a est divisée par 2.", "correctAnswer": "Est divisée par 2"}, {"id": "q7", "options": ["Égales et opposées", "Égales et de même sens", "Différentes", "Proportionnelles"], "question": "Action et réaction sont:", "explanation": "3ème loi de Newton: les forces sont égales en norme et de sens opposé.", "correctAnswer": "Égales et opposées"}, {"id": "q8", "options": ["5 N", "49 N", "50 N", "500 N"], "question": "Le poids d'un objet de 5kg sur Terre est environ:", "explanation": "P = mg = 5 × 9.81 ≈ 49 N.", "correctAnswer": "49 N"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f	\N	[]	\N	f	0	0	0.00	\N	\N
3	Quiz - Techniques de Dissertation	3	7	medium	8	25	60	[{"id": "q1", "options": ["Thèse, Antithèse, Synthèse", "Introduction, Développement, Conclusion", "Cause, Conséquence, Solution", "Chronologique uniquement"], "question": "Un plan dialectique comprend:", "explanation": "Le plan dialectique oppose une thèse à son antithèse puis propose une synthèse.", "correctAnswer": "Thèse, Antithèse, Synthèse"}, {"id": "q2", "options": ["Résumer le texte", "Capter l'attention du lecteur", "Donner la réponse", "Citer l'auteur obligatoirement"], "question": "L'accroche d'une introduction doit:", "explanation": "L'accroche est la première phrase qui attire le lecteur.", "correctAnswer": "Capter l'attention du lecteur"}, {"id": "q3", "options": ["La question à laquelle répond la dissertation", "Le titre du sujet", "La première partie", "La conclusion"], "question": "La problématique est:", "explanation": "La problématique est la question centrale qui guide toute la réflexion.", "correctAnswer": "La question à laquelle répond la dissertation"}, {"id": "q4", "options": ["Relier les idées entre elles", "Décorer le texte", "Remplacer la ponctuation", "Citer un auteur"], "question": "Un connecteur logique sert à:", "explanation": "Les connecteurs assurent la cohérence et la progression du raisonnement.", "correctAnswer": "Relier les idées entre elles"}, {"id": "q5", "options": ["1", "2", "3", "4"], "question": "Combien de parties minimum dans un développement ?", "explanation": "Un développement comporte au minimum 2 parties pour confronter des idées.", "correctAnswer": "2"}, {"id": "q6", "options": ["Répéter l'introduction", "Répondre à la problématique et ouvrir", "Ajouter de nouveaux arguments", "Citer tous les auteurs"], "question": "La conclusion doit:", "explanation": "La conclusion synthétise la réponse et propose une ouverture.", "correctAnswer": "Répondre à la problématique et ouvrir"}, {"id": "q7", "options": ["N'importe quelle source", "D'une œuvre étudiée en rapport avec le sujet", "De Wikipedia", "D'un film uniquement"], "question": "Un exemple littéraire pertinent provient de:", "explanation": "Les exemples doivent être issus d'œuvres littéraires en lien avec le sujet.", "correctAnswer": "D'une œuvre étudiée en rapport avec le sujet"}, {"id": "q8", "options": ["Remplir le texte", "Assurer la progression logique", "Citer un nouveau texte", "Changer de sujet"], "question": "La transition entre deux parties sert à:", "explanation": "La transition résume la partie précédente et annonce la suivante.", "correctAnswer": "Assurer la progression logique"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f	\N	[]	\N	f	0	0	0.00	\N	\N
4	Quiz - Python Fondamentaux	9	11	easy	10	20	60	[{"id": "q1", "options": ["var x = 5", "int x = 5", "x = 5", "let x = 5"], "question": "Comment déclarer une variable en Python ?", "explanation": "Python utilise le typage dynamique, pas besoin de déclarer le type.", "correctAnswer": "x = 5"}, {"id": "q2", "options": ["3.33", "3", "4", "1"], "question": "Quel est le résultat de 10 // 3 ?", "explanation": "// est la division entière en Python.", "correctAnswer": "3"}, {"id": "q3", "options": ["list = ()", "list = []", "list = {}", "list = <>"], "question": "Comment créer une liste en Python ?", "explanation": "Les crochets [] créent une liste en Python.", "correctAnswer": "list = []"}, {"id": "q4", "options": ["echo()", "console.log()", "print()", "write()"], "question": "Quelle fonction affiche du texte ?", "explanation": "print() est la fonction d'affichage en Python.", "correctAnswer": "print()"}, {"id": "q5", "options": ["// commentaire", "/* commentaire */", "# commentaire", "-- commentaire"], "question": "Comment écrire un commentaire ?", "explanation": "Le # est utilisé pour les commentaires en Python.", "correctAnswer": "# commentaire"}, {"id": "q6", "options": ["function", "func", "def", "fn"], "question": "Quel mot-clé définit une fonction ?", "explanation": "Le mot-clé def définit une fonction en Python.", "correctAnswer": "def"}, {"id": "q7", "options": ["typeof(x)", "type(x)", "x.type", "getType(x)"], "question": "Comment vérifier le type d'une variable ?", "explanation": "type() retourne le type d'un objet en Python.", "correctAnswer": "type(x)"}, {"id": "q8", "options": ["1", "2", "3", "4"], "question": "Quel est le résultat de len([1,2,3]) ?", "explanation": "len() retourne le nombre d'éléments dans une liste.", "correctAnswer": "3"}, {"id": "q9", "options": ["str1 + str2", "str1 & str2", "str1.concat(str2)", "concat(str1, str2)"], "question": "Comment concaténer deux strings ?", "explanation": "L'opérateur + concatène les chaînes en Python.", "correctAnswer": "str1 + str2"}, {"id": "q10", "options": ["for i in range(10):", "for(i=0;i<10;i++)", "foreach i in 10", "loop i to 10"], "question": "Quelle structure pour une boucle itérative ?", "explanation": "Python utilise for...in avec range() pour itérer.", "correctAnswer": "for i in range(10):"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f	\N	[]	\N	f	0	0	0.00	\N	\N
5	Quiz - English Basics	5	15	easy	8	15	60	[{"id": "q1", "options": ["Goed", "Went", "Gone", "Going"], "question": "What is the past tense of 'go' ?", "explanation": "Go is an irregular verb: go → went → gone.", "correctAnswer": "Went"}, {"id": "q2", "options": ["She don't like coffee", "She doesn't likes coffee", "She doesn't like coffee", "She not like coffee"], "question": "Choose the correct sentence:", "explanation": "Third person singular uses doesn't + base form.", "correctAnswer": "She doesn't like coffee"}, {"id": "q3", "options": ["Present Simple", "Present Perfect", "Present Perfect Continuous", "Past Simple"], "question": "'I have been studying for 3 hours' is in which tense?", "explanation": "Have been + -ing indicates ongoing action from past to present.", "correctAnswer": "Present Perfect Continuous"}, {"id": "q4", "options": ["Childs", "Childes", "Children", "Childrens"], "question": "What is the plural of 'child' ?", "explanation": "Child → children is an irregular plural.", "correctAnswer": "Children"}, {"id": "q5", "options": ["I like dogs", "If it rains, I will stay home", "She is tall", "They went to school"], "question": "Which is a conditional sentence?", "explanation": "If-clause + result clause forms a conditional sentence.", "correctAnswer": "If it rains, I will stay home"}, {"id": "q6", "options": ["Noun", "Verb", "Adjective", "Adverb"], "question": "'Beautiful' is what type of word?", "explanation": "Beautiful describes a noun, making it an adjective.", "correctAnswer": "Adjective"}, {"id": "q7", "options": ["in", "at", "on", "by"], "question": "Choose the correct preposition: 'I arrived ___ Monday'", "explanation": "We use 'on' with days of the week.", "correctAnswer": "on"}, {"id": "q8", "options": ["Rare", "Present everywhere", "Fast", "Invisible"], "question": "What does 'ubiquitous' mean?", "explanation": "Ubiquitous means found or existing everywhere.", "correctAnswer": "Present everywhere"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f	\N	[]	\N	f	0	0	0.00	\N	\N
6	Quiz - React Hooks	26	11	hard	8	25	60	[{"id": "q1", "options": ["useEffect", "useState", "useContext", "useReducer"], "question": "Quel hook gère l'état local d'un composant ?", "explanation": "useState retourne une paire [valeur, setter] pour gérer l'état local.", "correctAnswer": "useState"}, {"id": "q2", "options": ["Avant le rendu", "Après le rendu", "Pendant le rendu", "Uniquement au montage"], "question": "useEffect s'exécute:", "explanation": "useEffect s'exécute après chaque rendu (par défaut).", "correctAnswer": "Après le rendu"}, {"id": "q3", "options": ["Utiliser useCallback", "Passer un tableau de dépendances", "Utiliser useMemo", "Appeler useEffect deux fois"], "question": "Comment éviter un re-render inutile avec useEffect ?", "explanation": "Le tableau de dépendances contrôle quand l'effet se re-exécute.", "correctAnswer": "Passer un tableau de dépendances"}, {"id": "q4", "options": ["Gérer le state", "Accéder aux éléments DOM", "Créer des routes", "Gérer le CSS"], "question": "useRef est utile pour:", "explanation": "useRef crée une référence persistante, souvent utilisée pour accéder au DOM.", "correctAnswer": "Accéder aux éléments DOM"}, {"id": "q5", "options": ["useState", "useReducer", "useContext", "useCallback"], "question": "Quel hook remplace Redux pour des states complexes ?", "explanation": "useReducer gère des états complexes avec un pattern reducer (action/dispatch).", "correctAnswer": "useReducer"}, {"id": "q6", "options": ["Optimiser les performances", "Partager des données sans prop drilling", "Créer des effets de bord", "Mémoriser des calculs"], "question": "useContext sert à:", "explanation": "useContext permet d'accéder au contexte sans passer les props manuellement.", "correctAnswer": "Partager des données sans prop drilling"}, {"id": "q7", "options": ["Mémoriser un calcul coûteux", "Gérer les effets", "Créer des refs", "Gérer le routing"], "question": "useMemo est utilisé pour:", "explanation": "useMemo met en cache le résultat d'un calcul entre les rendus.", "correctAnswer": "Mémoriser un calcul coûteux"}, {"id": "q8", "options": ["Les appeler dans des boucles", "Les appeler au top level du composant", "Les appeler dans des conditions", "Les appeler dans des callbacks"], "question": "Les règles des Hooks imposent de:", "explanation": "Les Hooks doivent être appelés au plus haut niveau, jamais dans des boucles ou conditions.", "correctAnswer": "Les appeler au top level du composant"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f	\N	[]	\N	f	0	0	0.00	\N	\N
7	Quiz - Système Nerveux	8	3	medium	8	20	60	[{"id": "q1", "options": ["Globule rouge", "Neurone", "Plaquette", "Lymphocyte"], "question": "Quelle cellule transmet l'influx nerveux ?", "explanation": "Le neurone est la cellule fondamentale du système nerveux.", "correctAnswer": "Neurone"}, {"id": "q2", "options": ["Un os", "La jonction entre deux neurones", "Un muscle", "Une glande"], "question": "La synapse est:", "explanation": "La synapse permet la transmission du signal entre neurones.", "correctAnswer": "La jonction entre deux neurones"}, {"id": "q3", "options": ["Système nerveux périphérique", "Système nerveux central", "Système endocrinien", "Système digestif"], "question": "Le cerveau fait partie du:", "explanation": "Le SNC comprend le cerveau et la moelle épinière.", "correctAnswer": "Système nerveux central"}, {"id": "q4", "options": ["Du noyau", "De l'axone", "De la synapse", "Des dendrites"], "question": "Les neurotransmetteurs sont libérés au niveau:", "explanation": "Les neurotransmetteurs sont libérés dans la fente synaptique.", "correctAnswer": "De la synapse"}, {"id": "q5", "options": ["1 m/s", "10 m/s", "Jusqu'à 120 m/s", "1000 m/s"], "question": "L'influx nerveux se propage à quelle vitesse ?", "explanation": "Les fibres myélinisées peuvent conduire l'influx jusqu'à 120 m/s.", "correctAnswer": "Jusqu'à 120 m/s"}, {"id": "q6", "options": ["Nourrir le neurone", "Accélérer la conduction nerveuse", "Produire des hormones", "Détruire les virus"], "question": "La myéline sert à:", "explanation": "La gaine de myéline isole l'axone et accélère la propagation du signal.", "correctAnswer": "Accélérer la conduction nerveuse"}, {"id": "q7", "options": ["Les mouvements volontaires", "Les fonctions involontaires", "La pensée", "La mémoire"], "question": "Le système nerveux autonome contrôle:", "explanation": "Le SNA gère le rythme cardiaque, la digestion, etc. de façon involontaire.", "correctAnswer": "Les fonctions involontaires"}, {"id": "q8", "options": ["10", "12", "24", "31"], "question": "Combien de paires de nerfs crâniens ?", "explanation": "Il existe 12 paires de nerfs crâniens.", "correctAnswer": "12"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f	\N	[]	\N	f	0	0	0.00	\N	\N
\.


--
-- Data for Name: RefreshTokens; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."RefreshTokens" ("Id", "UserId", "Token", "ExpiresAt", "RevokedAt", "CreatedAt", "RevokedByIp") FROM stdin;
3	17	53JTyZnL20O9hgL2py57li+SSgrbVXEDGpgoHNWQ9QdBOycXTYPPuADuiOsBFBV07Ws1xd/0QsjloR4Fftxzrg==	2026-06-21 18:43:28.136062+00	\N	2026-06-14 18:43:28.136087+00	\N
4	17	RzfnAklT4UEjuKRT5asiK5VLXddPk8mvCAj9bzIz3km1EaVNHW9ftqOOjVqdEIRJWfZKU5zAXONucYHd9Xy50Q==	2026-06-21 19:38:31.767098+00	\N	2026-06-14 19:38:31.7671+00	\N
5	21	hu3ePdYRo2kQ1WuMwwWS5F5uPerSiVkkYzdILA1zE51AOVm64zdHhW7zvRp+K9sPsCSIN9GtCjAEGHH+fPn6pg==	2026-06-21 21:37:59.927103+00	\N	2026-06-14 21:37:59.927133+00	\N
6	21	PhqWX3hpYp7nA7xQuzNcD7egisGh73XDP8YVl9yq12DdPGdMssGUtZp2PdDf5lndSEOTz2xwcWLw5/S7R2SJ5w==	2026-06-22 01:24:57.570694+00	\N	2026-06-15 01:24:57.570695+00	\N
7	21	kMqquafxPfraLrItibUDAhhjGjQgxyByKPOKfKfApsLWlFkX7A8eosA47Rf4sNiXbTGDO95tpIabpWOdpBtp3A==	2026-06-22 06:33:58.844457+00	\N	2026-06-15 06:33:58.84449+00	\N
8	17	YtshxUS7xuaN4iSqc18EisNF2F9i2e8U2oDMukDBa8ZTe2DoFOu3uEj0uet4aKHbQtlPd+qpnUcj4uVLjrNuaQ==	2026-06-23 15:41:55.988411+00	2026-06-16 15:48:45.032518+00	2026-06-16 15:41:55.988443+00	\N
9	21	cnuDiuNO0ZgnRNRYhPrPxQEwSmISTf5X2Ckiia+jeAhxA37NdYtDaCbkUzhUy7+YFASv3LU5vTi0pi6GYuinzQ==	2026-06-23 15:49:05.621928+00	2026-06-16 15:49:52.446344+00	2026-06-16 15:49:05.621931+00	\N
10	20	sK179TtDMlppmjWfYVlrA/jWhAoSum/iTeF68Jv7caKMv5F7QTqWKSp8MOU42ubfwPegJsy//1lYkF90lBDR9A==	2026-06-23 15:50:10.600183+00	2026-06-16 15:57:05.919732+00	2026-06-16 15:50:10.600184+00	\N
12	18	C/lpsTrHhCHpHjNdBo9hJxHCXxh1nogq7uO1ANpcv5/2ysvG6gfyy3vT4Aumny3WcbbQnmSo2yKd8plBAm8kOw==	2026-06-24 10:14:10.678801+00	\N	2026-06-17 10:14:10.678802+00	\N
11	21	iiytDof53NxwOfji3AQmAddJ5O7k3gb3zkK7bBkgrXwDBUQ0JMqrN34VPLiWQrNcHdgkT6UZ9A/yxhr9adiqCQ==	2026-06-24 10:12:48.936776+00	2026-06-17 10:15:35.988728+00	2026-06-17 10:12:48.936801+00	\N
13	21	VaieTsII1OyWcfiq5EoRrnJCK0lxiW42Nod1q+N347OKvwPDBMEG5+uKel6uFCaDj4Ssbbn/MGkURrqD5NdWJw==	2026-06-24 10:15:35.988726+00	\N	2026-06-17 10:15:35.988727+00	\N
14	20	qnpF/3Dz6uo2yIJ3VK4C+SXak10WXs2Qqw20XurxKMKqVcw5euKvQhHSuZOtYpwsjAuXQaXB+RAX95dzZ8cjoA==	2026-06-24 15:25:51.411208+00	2026-06-17 15:27:02.417034+00	2026-06-17 15:25:51.411209+00	\N
15	20	XOvwAAEV4AsDR2VNbOWJJRR30XoFXFh+/iibnLU7rtZMJxUCzvl4H0R7KyyT8rvMLiYwuvq/NVpZjCUezr9Nvg==	2026-06-24 15:27:02.417032+00	\N	2026-06-17 15:27:02.417034+00	\N
16	21	sE1FjiJf2xIZ83n8NK27f5kH+lIZh849k5IuewIe8/aY9GJF70GL+FibU5tlg/075r5MsSdaQrKVK06iC288pQ==	2026-06-24 15:28:01.551569+00	\N	2026-06-17 15:28:01.551571+00	\N
18	21	OO8jYiDOW+OpNMfV/sOEi8IoicDZJcahtJPJ1YVPPT2cV4U6fd6HQqVgBIDZnpAA62rhP7FIqEN/ITSWfwZ19Q==	2026-06-29 13:14:02.770749+00	2026-06-22 13:15:20.553963+00	2026-06-22 13:14:02.77078+00	\N
19	21	Z5LdmQxkKvy8xHdl5zHvWgyoRWRsAX1T65QTvzwJLCKbjbmgl67vbBE4b55iw0BoqUgCL0Yct7k3xJJlBbWj5A==	2026-06-29 13:15:20.553961+00	\N	2026-06-22 13:15:20.553962+00	\N
20	21	OkEi2yOR7+lKk3gn4aBsog8L/wxYYTScoFovfJgRmkYFta1ZIDkEIr7/cnq5vMdAQ+V6tvJyeBVGKbC1mKU8Vw==	2026-06-29 13:19:56.224172+00	\N	2026-06-22 13:19:56.224173+00	\N
21	21	EXhWd8R6jobSvA8XowbRjsa4KgWudjPa+yS0h56LrmNK8TOneUWuPukTReTvY6aettxkS24pSPTWKrKJt9aQzQ==	2026-06-29 14:16:01.109875+00	\N	2026-06-22 14:16:01.109877+00	\N
22	21	YQ2osJ+5zh5MV69JH/If0KcR0b0PxIhtAGDqAb+v83eJtRx4/cTNL8FqAzUclCRHX973qm+/kJtnRBfHJ6suQw==	2026-06-29 14:22:16.730618+00	\N	2026-06-22 14:22:16.730619+00	\N
23	21	KiKbjb2xujSCmEG+pDjIg0Zl4oncDh8Oq91mTS90V1teNPztjWDBlazglG9/gP5AxDgglT+hRsgy9T4IIFU1RA==	2026-06-29 14:34:47.818385+00	\N	2026-06-22 14:34:47.818386+00	\N
24	21	8G0cfFscb4i8H1UwSE3DOTc0WSYAG2cmnQ7GIAn2iZFZ/PSPePkg9to2nVFM3ZRZU2zItVw5VI6Ur3HGMu2IfQ==	2026-06-29 14:43:47.724953+00	\N	2026-06-22 14:43:47.724954+00	\N
25	21	CBjmSaWgoxJy5yDemDhVaf//nua8iRVOZ4L9VO6Kj3PV9IgzLyIqZXuYQTSCelJRdk/UauyCWzNtnJxFz2HgLA==	2026-06-29 15:01:08.439295+00	\N	2026-06-22 15:01:08.439296+00	\N
26	21	RuYdLX0OnNG+asWzUk+Xxz48RqttJLHlxTPN84wMRvMJpUK4U2OWVWUFklG6zOCgb2+7gKpwGWVSuAvTsgGAOQ==	2026-06-29 15:02:52.994415+00	\N	2026-06-22 15:02:52.994418+00	\N
17	17	GSZtkdoPvnvuzmqrpRDJwZJizgOeSblflgJlzyT4OILJ7lT5/zRPpHI/JV7E2f0a5mtlCDVP16sB46dNVpEfSQ==	2026-06-24 15:32:47.44873+00	2026-06-22 15:58:43.351845+00	2026-06-17 15:32:47.448732+00	\N
27	17	sTqPib99/dIK9zMTAKsTRClaxteK9Uq4036kuuTvntBV2mWYARbajvl8m8OdJx1Y7gTRei4hIaHYUIMGmnabXw==	2026-06-29 15:58:43.351843+00	\N	2026-06-22 15:58:43.351844+00	\N
28	20	Ypx/An6KMyNpTsiuLsp7CK5nmFoBA1SeFLvwyViV1AYhfwwtiJv3xgz/F7zDttFurD8d7mzKqvqTNECeEQkgQw==	2026-06-29 16:00:02.548424+00	\N	2026-06-22 16:00:02.548425+00	\N
29	18	V46i5COfVGmXRiYHfCZpxWzZsv0UCm0QHyIpExl/qLcIrgIoTjKIHC7iv00yA5a17Z4P0vBlru7pbvqRO1Gl4A==	2026-06-29 16:05:22.927348+00	\N	2026-06-22 16:05:22.927349+00	\N
30	21	ZDFtvQVjzBbcT0LcrSqc2uy/ZCfaUFQ4Ma59rwotDu5SLaadft2tm+ATyT+TgMxi09tmV/KJhispPnrRJ+qWkw==	2026-06-29 19:30:11.808687+00	\N	2026-06-22 19:30:11.808688+00	\N
31	21	fsN90f13QSSb2ISC52neOiCZODjR3xW2r3SBAM13G14tjwOBEL9KcxFh9xH+FzFMhw9E4Si8zPrlwsZEanjNbg==	2026-06-29 19:42:04.360022+00	\N	2026-06-22 19:42:04.360024+00	\N
32	21	UjWyO4XZdGUExaf/I3WlhcKt4Uxb3m6XTAEnBNIs28AXtNkpT1Ky239RtzAebK9kHUjC3FPmOGuFhH8U/9XeAg==	2026-06-29 19:54:31.454848+00	\N	2026-06-22 19:54:31.45485+00	\N
33	21	KerEPLO+Yy8/sIr8lSPdyLkEeRA9U8rzhxPYs6QSkZFaHjKhbM1PL1w2GZ2ZA5i+vtZkw2dQKyE0c/FywRdJTg==	2026-06-29 19:55:48.205988+00	\N	2026-06-22 19:55:48.205989+00	\N
34	21	NOILCvU+r1926ef+r7aSAJ+vhqsHPp0aoLFrOtKXmnSYVOXxVYlQscMCmao8Dt53A8/fOEPEr3q6MJoD3dRWmw==	2026-06-29 19:58:20.227881+00	\N	2026-06-22 19:58:20.227882+00	\N
35	21	O7+FWFRVNg27jpoAYjTrkdAhk42yusHC8zKHw9fmg2ZmP2V4jMO2uh/dJzT9l1coQ+lWzGYnQgvIfcisQy/O1Q==	2026-06-30 10:20:03.042199+00	2026-06-23 14:46:39.186763+00	2026-06-23 10:20:03.042232+00	\N
36	20	1z2mYclmSNivo9O9HgVKDIGcRM1vkLNrdpxgd49/FJvjEkksWXXNsYuWSM1rB6wzzd32gj+FXsmC8S53vCxVmA==	2026-06-30 14:47:23.752505+00	2026-06-23 14:50:01.658711+00	2026-06-23 14:47:23.752507+00	\N
37	17	OX1VTWyJbbX+rHr86Z/n4JXZ62tuH4cBPFFg0YiybX0UGK8qoGyvze+X5LO+YJS4UYvFY+oBx4ffJIAgt/dQpg==	2026-06-30 14:50:09.84646+00	2026-06-23 17:03:55.40906+00	2026-06-23 14:50:09.846464+00	\N
38	21	GgK4/WoJYxjhUgOlR3dobFQSgrBTcvCaHLJepWQwftNe1gkvPejbsaXjFPAsKWipctG+SX34+0ZKzZ432AF46Q==	2026-06-30 17:19:30.433711+00	2026-06-23 17:20:13.843414+00	2026-06-23 17:19:30.433742+00	\N
39	17	9jb8/YQXvdeEBmJP2KutKvIZQvus47Xs60yfohPVKhHTsuVQ2cDLgdoOykBYulR3103uV6EYZbGQL1x5hXKACQ==	2026-06-30 17:20:34.312343+00	2026-06-23 17:25:09.790624+00	2026-06-23 17:20:34.312345+00	\N
40	17	sJ/39qEdwRQvdtHMBGXnt9pQI9c+VyDw0iIh4CcL4oBBBV2+3H/gi8mU5OLi32bOKACsf5xkuFqmwRKYyxvrZA==	2026-06-30 17:25:09.790622+00	\N	2026-06-23 17:25:09.790624+00	\N
42	21	uDFY93PCY602vDEI8gKEfnZCHL+6EPLoLUyZPapPVRvuSXAyK42C0HL88MAJAnIcrQFY6alUD5N2qwFKgUMZgg==	2026-06-30 17:40:46.139854+00	2026-06-23 17:42:39.072806+00	2026-06-23 17:40:46.139856+00	\N
43	21	LgvXPu0DLz4ssCLQCiUBGC9mQlnpvtfgpzm9GLUxnEyalEHvnufX9a1mhhhLZdXmzLzN+RenYg/CcI57gwQifg==	2026-06-30 17:42:39.072805+00	2026-06-23 17:42:44.606635+00	2026-06-23 17:42:39.072806+00	\N
41	17	Ipq3viZ1/bVQXEekQS9TioXCcTk8J0Bsq0AOnFRrNi03pZUIxcFoy+WGuc+a7ZUqlWs539kJVGaXYxtNYNniTw==	2026-06-30 17:39:12.67589+00	2026-06-23 18:05:19.612071+00	2026-06-23 17:39:12.675891+00	\N
45	17	3LUpO9OS9dkrGgu2V/M+2+sMYeTCI7qMimtXh7PlQhqNFJUElVz9YrE4/ip9VAkHJfCYWBB58DrwDGjFWWTBXg==	2026-06-30 18:05:19.61207+00	2026-06-23 18:05:21.786491+00	2026-06-23 18:05:19.612071+00	\N
46	17	axt7zG5rbCFgbuxzAfwZcSdSusclllxK9XuS3puiBJKBu+pyD77F6y9/KmO6Kns1XNu+JFl3oczQK8xa+xW8Wg==	2026-06-30 18:05:21.786489+00	\N	2026-06-23 18:05:21.786491+00	\N
47	17	paOkdpf6+In2XhRJoZT7i2fpvmgkvHWBbxhwgKV+Iw+faHumTCrI5rBMLvFVruqitf97feV1NvK4LgMYXth/Mg==	2026-07-01 09:18:44.146628+00	\N	2026-06-24 09:18:44.146654+00	\N
48	17	7/9W9easfA8mHqb5VortMJcdX9ho5fU5t3DgeAPo5EnKNc69A1FJTlSe3meGf88rTpLhz3C/emnRj/4e0LPuJw==	2026-07-01 11:05:38.359256+00	\N	2026-06-24 11:05:38.359289+00	\N
49	21	5N76KnZJcoE3mzm+iTOXv1G5weM18//tmrQhmoneQTeuAJWBAVBoNbsOiNMy+aPubXpCWoKv2OYJf9vkm75G0A==	2026-07-06 14:28:14.603506+00	2026-06-29 14:29:32.755568+00	2026-06-29 14:28:14.603535+00	\N
52	17	b2NQ6Bk9UowhJMzszFthWMouWj1deFIPDMs2nM8bCrPHI12Qm9EuoqZagVM7wbzFdYi69a5J+y3qbevJwDoNBQ==	2026-08-11 07:29:31.941674+00	\N	2026-08-04 07:29:31.941701+00	\N
50	21	MThaLtcTalAQ9K7wwU1GL6oEbaCZSkIzVzGzXg2QkaC/oC6NCjOcJQbDfBOINCBaVSURVQMc/cljKNgLaOwG0Q==	2026-07-06 16:23:16.235741+00	\N	2026-06-29 16:23:16.235773+00	\N
44	21	y+Sa4nB7gAYdpSbFOeILVSjrGJ04GURK8rn1nZeL9jiIZHlw3Gf0XOIiHnHPEGI8xm4u97VpoESC9wMu5CPqUA==	2026-06-30 17:42:44.606634+00	2026-06-29 16:23:16.236418+00	2026-06-23 17:42:44.606635+00	\N
51	21	CHZE6XWJFvkYQzTrE3KRR0Mo/8a5Kl/24FoyVnQtpiyO7ZgV8y8zBwc5rQeYU3YfQ8UiKjUU2LiO8ef51u+THw==	2026-07-06 16:23:16.236417+00	\N	2026-06-29 16:23:16.236418+00	\N
53	30	cV1sKAXarg9J6sS9nxSWACCBgc/rbPGFXh1vf2KJJTpEenCehLv/DS3GoLeLMWSyYLfWQ9PqBnz3sRoxsRvZhg==	2026-08-27 06:53:43.213602+00	2026-08-20 07:03:13.007778+00	2026-08-20 06:53:43.213639+00	\N
54	30	2zG9jEnO9LTKVCl7+jVUV3jSP5AOvX648aMMxoAdKfwAYULxHAJZTAfhgsgmF0uhJDqEuxuPVY+tasD+OMOdaw==	2026-08-27 07:03:13.007776+00	2026-08-20 07:03:17.267918+00	2026-08-20 07:03:13.007777+00	\N
55	30	/SH57ewYtQLym+y47n3T/1AxSLfw0S6R+3niEFcOajKHPD8nXdWbVaT31EidgyGLu62o5F3kcaxMphxihuHqnA==	2026-08-27 07:03:17.267917+00	\N	2026-08-20 07:03:17.267918+00	\N
56	31	6Uw/8Sv1EXwqMg29ZNw5M9tblzPFHotODtQYcmGVbRqmnqpasLXRxY7z9zDPYySLvekcbH8sE0rSYHTa629cMw==	2026-08-27 10:43:58.009577+00	2026-08-20 10:45:17.569046+00	2026-08-20 10:43:58.009614+00	\N
57	31	ljcgWP14uuE7XbRIRv7qUfcbz8GCUdk+smoaS5mtgIkUzggSKIwnht6Du0nrMmZwj+D0tOYUcmHnRMoiiWRcIA==	2026-08-27 10:45:17.569045+00	2026-08-20 10:45:20.914854+00	2026-08-20 10:45:17.569046+00	\N
58	31	ZXt9fc1ge+8CrZiIlYSSO+k8AKZoP90kp3xrPVgZ2oZMdlWRX637sb9tMQ4xrl2j0qhWDG2ui9Aw95VprYivsA==	2026-08-27 10:45:20.91347+00	\N	2026-08-20 10:45:20.913471+00	\N
64	36	6Vrr6tA8K/7x4CS9GIgbK65ud67WjFjl+Og3iIyDCLlG8kTLlah9Zr8Rnagip3vUpYr5yYAgpt9zH2dOHU/l7w==	2026-09-01 15:44:25.332713+00	2026-08-25 15:53:33.257884+00	2026-08-25 15:44:25.332742+00	\N
84	37	4MVDk8iv1EZ5Xj1GvJPgh7vkJzXy2G6mcRI/hxBvvPkgeELk170ewLeu33bcZem5IPvLE1zwcjtO4FYN7CSUXw==	2026-09-05 12:22:34.089393+00	2026-08-29 13:45:13.214179+00	2026-08-29 12:22:34.089393+00	\N
67	37	q0cZTZGWTjtcQYavMAp91mBFgvnUxLDDA956Z8LYKd9UXUpQPH9TP9jfZeWBu42P9gzHNlr6OQvrctN/nrcwag==	2026-09-03 09:13:45.750084+00	2026-08-27 09:51:15.394131+00	2026-08-27 09:13:45.750086+00	\N
65	36	L2dajiSMzk1kI0Z2qC/d3YsBJnGzKsryh3A44PTKuUp3hpsO2AP+TAkGDd4wf2RKBZ9Jqb5NKLDRU2WPZblcXA==	2026-09-01 15:53:33.257883+00	2026-08-27 12:46:32.148218+00	2026-08-25 15:53:33.257884+00	\N
69	36	xUlepIBg2ZoFBwq/C7KlJ7dpHI85RH6F+j4p347/n3uG2Yk8UopqcmSubfTWgHCKr0Z/AzeHv0vGiyzOiZrrjg==	2026-09-03 12:46:32.148216+00	2026-08-28 06:40:05.373975+00	2026-08-27 12:46:32.148217+00	\N
71	37	NyxeuTAekZIFSBU8q+OUnoOat+tG+zaZnwIQALFKe8HHAMGtociqRe8Usb6Fp2zNxqapWHSBhgczfUfxS6q2qw==	2026-09-04 07:51:39.076051+00	2026-08-28 08:25:00.772737+00	2026-08-28 07:51:39.076051+00	\N
70	37	kf9hm4O6mE1tXctUqWwrQ3xJPtwAjFDpHSxQIqcRoBDrw90VsGP1aEUaFFCrEy7Jkxp6LNm4gab/ZzRuWk8LOg==	2026-09-04 07:50:21.282773+00	2026-08-28 17:03:26.382523+00	2026-08-28 07:50:21.282774+00	\N
74	37	U3TRyp5EL2OPw2iS2PL/ErRbqU8+9k+xIGzx7rRZ86qdsmg4oNlGDoR2dQ5dy9C/yXEFByMojnnzoUomommIWg==	2026-09-04 17:03:26.382451+00	2026-08-28 17:04:59.908115+00	2026-08-28 17:03:26.382493+00	\N
75	37	J6GUBwDXtxtruY4gzh5qlCMmrJLcgmlrSwUoiKvSdy3ja9aJhxBWAYejebTPaplj0ZZ9Q265yhaabi1jfMuFyg==	2026-09-04 17:04:59.908113+00	2026-08-28 17:29:57.119792+00	2026-08-28 17:04:59.908114+00	\N
76	37	eGs+2VKkqfFaNAw8Zft1tUU4NHFI1wGzAu47fRD8wygKUkGzIIV/DwbibaMtTvCa5Bn5JjvtoGpDyr48LpNlKQ==	2026-09-04 17:29:57.11979+00	2026-08-28 17:29:59.144152+00	2026-08-28 17:29:57.119792+00	\N
77	37	ublZGSqZ43mYEmi4bFHYAsEAfXWly1HxBTFBdz42GNvBIr/dyiuU15L4i+1sOl8/8gdELt/dQ+iJqY6F+2DfAg==	2026-09-04 17:29:59.144151+00	2026-08-28 21:53:45.773878+00	2026-08-28 17:29:59.144152+00	\N
78	37	OvKhIHKIjLcXN3R4qJkNQo92ajUfPl3PTyM77rHwcJSTzCgoTtkyZ3z/P+viMxZFqnpI29Plr6CX7q2R7s767w==	2026-09-04 22:03:07.640341+00	2026-08-28 23:15:13.406386+00	2026-08-28 22:03:07.640342+00	\N
85	37	jY2K8w3UGVt40zMK5SQ9INEBaDByDZL/UeijTcWUK+n0MGQSEowBQjfaoTfe6sNFevVUykmtOodFFF09gELYcg==	2026-09-05 12:50:54.341869+00	2026-08-29 13:47:53.451769+00	2026-08-29 12:50:54.34187+00	\N
80	36	EyEiUw+ozB3DH2lpwEf9nk1PaHElHkaOix2E3HfN8X7q5fN26GExL26dZGVnwP0awEuJ3PUGUFUwLilkVu2UJw==	2026-09-05 09:43:18.638411+00	\N	2026-08-29 09:43:18.638439+00	\N
72	36	8vkEoLVVT5sdtqkm64MU4/9NE2J2UcTW93+SJWEYtyYWo5Ddw36LsZQBqdu/wNrihI2vXMcefLXSLn33BAkRow==	2026-09-04 08:15:28.867887+00	2026-08-29 09:43:18.63873+00	2026-08-28 08:15:28.867888+00	\N
81	36	UGSgy0POE0mauLub/NjWxeezyxPQ2j2rtq4kap4mZCBpuNu9G0175+arRZC6J1K5RWQhrlc0O5BDmbRWktbzOQ==	2026-09-05 09:43:18.638729+00	\N	2026-08-29 09:43:18.63873+00	\N
79	37	mGMJKmFqKqqyTa/JlCsBCenAv1VMLrLXr/FR2i/1xo7eP6wH1yMl4hgqs33ThPi5ebOgVleqrr9yl3Ia0DIBew==	2026-09-05 08:10:03.897987+00	2026-08-29 09:55:27.111732+00	2026-08-29 08:10:03.897987+00	\N
83	37	RMzFJJRtz27uFV11LDsae3p5VhDSYSAWiKnO50CxNKc/TXQf6DTFu48BarMQsb0RUmGALLAuiAFbHBTdDNljpg==	2026-09-05 10:02:41.136423+00	2026-08-29 10:08:15.028403+00	2026-08-29 10:02:41.136423+00	\N
86	37	KRUbfZHJSUAhTBdAGnIQJMvkpbDtt8QVIwtVlAGuRbhz4E3xyio9tR0tQq5kBUibFdgCUdTs6oJDYyM/NAX3rw==	2026-09-05 14:55:05.516605+00	2026-08-29 19:38:22.063916+00	2026-08-29 14:55:05.516605+00	\N
90	17	0FehmdBY9cMZc0xgth5h6gkL7B08y5fdLB+3MSEwsdDyVkL7SuWi6dONkVVBqW+qQkbCEWAtkxcKYZVweaWI3w==	2026-09-05 19:38:53.907152+00	\N	2026-08-29 19:38:53.907153+00	\N
88	37	PShaV0Ir/CiSDguymW+s1wBQPkAtWQWYs5yx+vc4xWjwCw2/a5XlHKJsEl5yczAmYBWf9mwyXpjZU20tzYydTA==	2026-09-05 17:50:38.848944+00	2026-08-29 19:41:21.72689+00	2026-08-29 17:50:38.848944+00	\N
91	17	4vYXgYDN25qt4+5pm2Qw8D5o+YcXfQ3DSwRvgjYNXK49tWO1YL4zgtXCpaC00DzdR5q7UoKo4SN8cYeqdbF/zQ==	2026-09-05 19:41:44.409538+00	\N	2026-08-29 19:41:44.409539+00	\N
89	17	CxaZbOyDifnFD9MonZUGIKKbYL2cFMsAa7cywsy9y2wwmry23Rwbprz43kTms7pT8FXSTnnvUqewtCbaM0PMTA==	2026-09-05 17:55:14.77879+00	2026-08-29 20:17:04.206679+00	2026-08-29 17:55:14.77879+00	\N
93	38	k/tVuXxlUSqtMsrCaVUloXEwLSo6Skjz2JlbnvsxT+rSEJxbNHkzjmn7WaweSmtcSIrVFc8pBJovVHTQyzl/fA==	2026-09-06 19:57:46.296737+00	2026-08-30 20:04:31.704587+00	2026-08-30 19:57:46.296737+00	\N
92	38	gsJlO106BVIDhm5SQqG9EOErY40xWZKS+KajrRTYDh0sGa74Pt6Cm9M/a/gcUdk7xno3qc/0+sz/T+1WyoOXzw==	2026-09-05 20:17:45.958204+00	2026-08-30 20:16:48.150808+00	2026-08-29 20:17:45.958204+00	\N
95	37	E631eXut5WvFz0ZEnaeX7JO2ptrfkceBrL16atemn+XYNxDYDyM7ba9JYjMqj9NUWT3RJwWotkOKuNpxId8Yng==	2026-09-06 20:13:01.918602+00	2026-08-30 20:38:50.266736+00	2026-08-30 20:13:01.918602+00	\N
94	37	1K25W/IbMqKOUiqEHfv9FlHtAjblrlG4NaljLuNDbOUjhIjUFljVe5qlpHdjUYQ3LdZJrc92uV20N1bER4qGGw==	2026-09-06 20:09:23.932655+00	2026-08-31 03:14:07.206904+00	2026-08-30 20:09:23.932656+00	\N
96	30	5UAd+aRs3PADh1HApeLDdScgxsMPbibJlpdtYbsZQaq6CmkmXUiTof4h1QaI4P+2SreR48ivbA3W6wMsZj8OCw==	2026-09-07 03:16:27.238109+00	2026-08-31 03:31:10.83268+00	2026-08-31 03:16:27.23811+00	\N
100	30	z63OZcZeSAA8wo2A6uB8nGLx7IShP3EVM0e3Rc/ZkPW9nSAkmLo4di9325tYFnABDFEmnuAYxP1czdIw7Aq4Kw==	2026-09-07 03:36:41.268819+00	2026-08-31 03:36:46.355658+00	2026-08-31 03:36:41.26882+00	\N
98	30	5gEfrtRXUwz//h+8iEkok1L872suX7tENksF3LhrP3VfTAH3M09AMf1zqUvf9nv29KzhrA+0qY54PUFOf0dBNg==	2026-09-07 03:36:41.268785+00	\N	2026-08-31 03:36:41.268786+00	\N
99	30	OPQc+VjbdQPxVaQwsQpEiwOFYGqlA6qnpNkiNeXiPge4rIx8YqKx+FcZr7qOqGyMd9QBjOh2xRePkuQeVtz5Jw==	2026-09-07 03:36:41.268536+00	\N	2026-08-31 03:36:41.268565+00	\N
97	30	mf/CA1hXpjKDoc9bWXxaVrmC2CshZfYjDbDBH8+8Ta+XjsWY4DXLWy7v4Ns/3l4wN+Az3rNRgLZ7Ilfd+1XVSQ==	2026-09-07 03:31:28.449857+00	2026-08-31 03:36:41.26882+00	2026-08-31 03:31:28.449858+00	\N
102	30	RAY5LtGKU6DphkEnsWAJ+e2jv78m2+Mqx6pLtkVjris0Ews7CrKf1pF92AJL88xyIpFqJfqWOU4yT/wrs3bMfA==	2026-09-07 03:42:11.141227+00	\N	2026-08-31 03:42:11.141227+00	\N
103	38	dSay284UwFbsygBw6v2RCnK9rSaSQkhnyK8RAIdNPqV+1q5dhQyoRzvbONN9D83uiG36YE21ZE/hDmiwpwUqfQ==	2026-09-07 10:01:24.229978+00	2026-08-31 10:03:27.466734+00	2026-08-31 10:01:24.229979+00	\N
68	37	4+o2CiWuv2Ym8cmxc64vZil9lql0LGILCcurCFbpign3PPVRdjkCQdJDIwVBOa0jMSxGgphu1ZzUPzw2gtDIWQ==	2026-09-03 10:00:55.507093+00	2026-08-31 11:16:56.49537+00	2026-08-27 10:00:55.507093+00	\N
73	37	7euhsHO1IXSc5cTL7p5BIXYMxV/Ls1lNBzzdLh5XdsA+L6SD2yNAPTAFk0Cc4dbU00KodVkm2dZQf4lJ9JWhhg==	2026-09-04 15:14:35.923765+00	2026-08-31 11:16:56.495409+00	2026-08-28 15:14:35.923766+00	\N
87	37	VXDX072YUR0twm7yWUjhz0S7Zch3/LDgi03Wkw5k1Tf7ZkN25UUT6q78/m5aI9uOI994RAMd1E/0l4NZgzzUBg==	2026-09-05 17:46:03.96504+00	2026-08-31 11:16:56.495409+00	2026-08-29 17:46:03.96504+00	\N
82	36	0mTBiGL5LkGAp7hFw0RZ14ZLvKUDlPAMH6NlA1jUUkW+4mN58UZsRv7zXzhSXEtvPdVES69tcUit+cuQQKiGWw==	2026-09-05 09:45:10.297039+00	2026-08-31 11:39:57.028495+00	2026-08-29 09:45:10.297039+00	\N
104	36	5d9v5pPvWOibeMOb13/nE9DKdfApDA2Io/N44HeWMms7cbzEkAPNi0nV1wYY2qnfBdSu4YL2lo2BgdkkPYOsww==	2026-09-07 11:39:57.02844+00	\N	2026-08-31 11:39:57.028473+00	\N
107	30	i5OIPNxosgQpwaj7iVz8+AxFCRMdNLTVReqSqop+xbUFTz2VmKRS8L1Gh9bH+pCvv5XPGh7VY95qpHGiakWcFw==	2026-09-07 16:10:06.972657+00	\N	2026-08-31 16:10:06.972657+00	\N
108	37	Q5Rv1qwASjlsfMwDqLNYs2h8GJvnysk4/f6Dx7Uy9gIHa3LN7hNu9ApS/MdPjK0KKXwpyh+vE6knj6Cl9EuP+g==	2026-09-08 06:18:54.283196+00	\N	2026-09-01 06:18:54.283196+00	\N
105	38	oz9qMyKjA6yaV4Y1GTEbQozSp0/hVKgl6LhePBZ4128Cj7zLcbHgKHcasRXWYxJz0h7kxc8e7qNdZr80U1R0Yg==	2026-09-07 11:40:18.476269+00	2026-09-01 06:36:24.802593+00	2026-08-31 11:40:18.476269+00	\N
109	37	1ZXfsTjcHkQogGZNdy/9n+rOBP8LXOYYx3ce6Ji/Hu/PkeMolvaYccRuyUk+re25YNN98UqLYZ4z9IJrLRRd/A==	2026-09-08 06:36:52.642321+00	\N	2026-09-01 06:36:52.642322+00	\N
110	39	x+iWWzerLsQj8nV0mucZx+Lml9+L6h+DZXI4E8PsvKvjRVeFgPDQTPOBjbko4pTSdp5BypLzQcRcvNx7Wqp4+Q==	2026-09-08 08:56:59.578258+00	2026-09-01 09:11:54.67413+00	2026-09-01 08:56:59.578308+00	\N
111	39	XeLIsZbOVUi8n+Dnyr5Tu4G4WMIwmGqwzL7Zuoo2YOs88UE6GpJgqfKVDfNTmiqid5FM3jgYzd94pscx0W1sUw==	2026-09-08 09:15:39.934779+00	2026-09-01 18:51:45.355032+00	2026-09-01 09:15:39.93478+00	\N
106	38	nq30B5vUIMjjDq+EO7TTZBTm/uy/Kdc4mdABd5XUUCNtNldeMBPhME+rwPTN2cjKE4A5SJII+JCQdW5eKLVTFw==	2026-09-07 13:18:10.703249+00	2026-09-01 13:36:17.970183+00	2026-08-31 13:18:10.70325+00	\N
112	30	ruMMBAr/ZJoM4bwwUFTtl32IMws2tiO/6BdFQMlhNreQLWvJjaApyc3EQu2fCiH7u/g6uNtqeRR4IrRnHEyEhQ==	2026-09-08 11:49:11.404068+00	\N	2026-09-01 11:49:11.404103+00	\N
101	30	Vzk4TML8Hxcid2K1857IJ4JkANaGuKYQmMW/l1Kbtz/Vb5AdzfRsinGxlO5gRsmXtiidD+5Vm2Hnpv3PLsVJ4g==	2026-09-07 03:37:04.098269+00	2026-09-01 11:49:11.404123+00	2026-08-31 03:37:04.098269+00	\N
114	38	lFQYMWEV5cgrd5wQglMRVHh0sxK2eOvPPat34pH28882N0lMLkb5HTnw8pPCNbr65qwBFvq63CjF3EEylsWMVQ==	2026-09-08 13:36:17.940007+00	\N	2026-09-01 13:36:17.940035+00	\N
115	38	ccUtiRVfNRXS7Ku21hxtWquq8YeHxvnP22yM4e9Av4ukwnZm4mKXf5uo3/TPWFCRsBRImKeXn/OxNdJ1X3eUHg==	2026-09-08 13:36:17.970183+00	2026-09-01 13:36:18.894935+00	2026-09-01 13:36:17.970183+00	\N
116	38	wQUq0PyAQcGDd7oF0vIE4vBMu49AtvMlRgVHjJ70ggRwNF3Pl7L4aM3nSYtS+OuzrHGeujtJAQd23Sfd5DILWg==	2026-09-08 13:36:18.894934+00	\N	2026-09-01 13:36:18.894935+00	\N
117	38	mXQwXVqgasKlBNSC0ktXNtE4y+4gNj8S6t/stJpR3w0If14GtfzdeO7b8vppjbKkrrb9nGZRFpr3B+Abk4Y7/w==	2026-09-08 13:36:42.508494+00	\N	2026-09-01 13:36:42.508495+00	\N
118	37	OaL9Dmn2M+HqBAJMuQSLjBCAvlNd3mVmBcMnVHJCkD4AnmmkwrGIYo3ACqVi79Xwxp5AyDNEC+4Y1JhbVLAI7g==	2026-09-08 16:34:12.662598+00	2026-09-01 16:34:27.107884+00	2026-09-01 16:34:12.662599+00	\N
119	38	wDsDqi9UHV7emx1n+xbPbRDglW/C6eW5mvMuLAkinfn0P11iZgS/L5AAjDtmgwXyP9fGD3FTIsLVfuqjy6Kksw==	2026-09-08 16:34:50.635558+00	\N	2026-09-01 16:34:50.635559+00	\N
113	30	rDg7NJkthiZ+fXXwA3Qp5UFGsUpl+yZrDfBcZ0ZKbXk2B3HDl8CZl7eky5ccxrRC1YGaIbAaWrhczHfoZQq6AQ==	2026-09-08 11:49:11.404059+00	2026-09-01 18:50:05.640929+00	2026-09-01 11:49:11.404094+00	\N
120	17	Mcs7LchdQhqWgM5KN6mOwyKB+SiW3Rkak2brKxCKYjVUqWv5K8NdWXd0czzQ2Y0dYyE1D157cQR1QQapSDtTFw==	2026-09-08 18:57:58.599267+00	\N	2026-09-01 18:57:58.599268+00	\N
121	38	gh8AxlM25JydZbxFkyOZqVebCstYDShwSZ4mWNw0QWElOkBjq2zL+/HhrZvQ2m9YIko2AAfu7Xwyy8Rk+OUNBA==	2026-09-08 19:13:17.625918+00	\N	2026-09-01 19:13:17.625919+00	\N
122	38	aK3hAn5P5Mzd+HdNW0AL8a2PdUlsoUU+zg/puV6PV7mYLl+fvy7YPbnHzsGuWL/jbm0objfqlmXZoYgjTvdbVg==	2026-09-08 21:58:10.088448+00	\N	2026-09-01 21:58:10.088448+00	\N
\.


--
-- Data for Name: Reviews; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Reviews" ("Id", "UserId", "SubjectId", "Rating", "Title", "Comment", "IsVerifiedPurchase", "HelpfulCount", "CreatedAt", "UpdatedAt", "IsDeleted") FROM stdin;
1	2	1	5	Excellent cours !	Les explications sont très claires, j'ai enfin compris les limites et dérivées. Je recommande vivement !	t	0	2025-12-20 13:09:38.395551+00	\N	f
2	3	3	5	Parfait pour le bac	Grâce à ce cours, j'ai eu 18 à ma dissertation. La méthode est vraiment efficace et bien expliquée.	t	0	2025-12-25 13:09:38.395551+00	\N	f
3	4	5	5	Mon anglais a décollé	En 3 mois j'ai progressé énormément. Les exercices sont variés et le professeur est très pédagogue.	t	0	2025-12-30 13:09:38.395551+00	\N	f
4	5	9	5	Python pour tous	Je n'avais aucune base en programmation. Maintenant je crée mes propres scripts. Cours incroyable !	t	0	2026-01-04 13:09:38.395551+00	\N	f
5	7	1	4	Très bon contenu	Contenu complet et bien structuré. Quelques exercices supplémentaires seraient appréciés mais globalement excellent.	t	0	2026-01-09 13:09:38.395551+00	\N	f
6	9	5	5	Incroyable !	Le meilleur cours d'anglais que j'ai suivi. On progresse vraiment vite avec la méthode proposée.	t	0	2026-01-14 13:09:38.395551+00	\N	f
7	12	26	5	React enfin maîtrisé	Après plusieurs tentatives ratées, ce cours m'a enfin permis de maîtriser React et Next.js. Bravo !	t	0	2026-01-24 13:09:38.395551+00	\N	f
8	13	9	4	Très satisfait	Cours bien structuré, les projets pratiques sont excellents pour consolider les acquis. Je continue avec JS avancé.	t	0	2026-01-29 13:09:38.395551+00	\N	f
9	2	8	5	Biologie passionnante	Le cours de biologie humaine est fascinant. Les animations et schémas aident vraiment à comprendre.	t	0	2026-01-01 13:09:38.395551+00	\N	f
10	3	20	4	Marketing très pratique	Des exemples concrets et des cas réels. J'applique déjà les stratégies apprises pour ma petite boutique en ligne.	t	0	2026-01-19 13:09:38.395551+00	\N	f
\.


--
-- Data for Name: RevisionEnrollments; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."RevisionEnrollments" ("Id", "UserId", "RevisionId", "AssociatedLearningHistoryId", "OriginalScore", "Status", "ProgressPercentage", "IsCompleted", "FinalScore", "ScoreImprovement", "AssignedAt", "StartedAt", "CompletedAt") FROM stdin;
\.


--
-- Data for Name: RevisionNotes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."RevisionNotes" ("Id", "UserId", "SubjectId", "Content", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: RevisionTags; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."RevisionTags" ("Id", "UserId", "SubjectId", "Label", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: Revisions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Revisions" ("Id", "Title", "SubjectId", "CreatedBy", "Content", "Summary", "KeyPoints", "DocumentUrl", "TopicCount", "Difficulty", "EstimatedDuration", "ViewCount", "IsPublished", "CreatedAt", "UpdatedAt", "IsDeleted", "Description", "Subject", "Topic", "ExamId", "Type", "VideoUrl", "DurationMinutes", "CreatedByUserId", "Status", "IsAutoAssigned", "TriggeredByScoreThreshold", "ImprovedScore", "Views", "Completions", "Tags", "PublishedAt") FROM stdin;
1	Fiche de révision - Limites	1	3	Rappel complet sur les limites de fonctions: définitions, théorèmes fondamentaux, formes indéterminées et méthodes de levée.	Maîtriser les techniques de calcul de limites pour le Bac.	["Définition epsilon-delta", "Limites usuelles", "Formes indéterminées 0/0, ∞/∞, ∞-∞", "Théorème des gendarmes", "Croissances comparées"]	https://docs.winplus.cm/revisions/maths/limites.pdf	5	medium	45	234	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
2	Fiche de révision - Dérivées et Applications	1	3	Dérivation: règles, dérivées composées, étude de fonctions, tangentes et optimisation.	Tout sur les dérivées: du calcul à l'application.	["Règles de dérivation", "Dérivée d'une composée", "Tableau de variation", "Tangente en un point", "Problèmes d'optimisation"]	https://docs.winplus.cm/revisions/maths/derivees.pdf	5	medium	50	312	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
3	Fiche de révision - Intégrales	1	3	Intégration: primitives, intégrale définie, calcul d'aires et méthodes d'intégration.	Des primitives au calcul d'aires.	["Primitives usuelles", "Intégrale de Riemann", "Calcul d'aires", "Intégration par parties", "Changement de variable"]	https://docs.winplus.cm/revisions/maths/integrales.pdf	5	difficile	55	189	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
4	Fiche de révision - Mécanique Newtonienne	2	3	Les 3 lois de Newton, applications aux mouvements rectilignes et circulaires, chute libre.	Toute la mécanique du programme de Terminale.	["3 lois de Newton", "Mouvement rectiligne uniforme", "Chute libre", "Mouvement circulaire", "Travail et énergie"]	https://docs.winplus.cm/revisions/physique/mecanique.pdf	5	medium	40	198	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
5	Fiche de révision - Énergie	2	3	Conservation de l'énergie, énergie cinétique, potentielle, mécanique. Travail d'une force.	Comprendre et appliquer la conservation de l'énergie.	["Énergie cinétique Ec=½mv²", "Énergie potentielle Ep=mgh", "Théorème de l'énergie cinétique", "Conservation énergie mécanique", "Puissance et rendement"]	https://docs.winplus.cm/revisions/physique/energie.pdf	5	medium	35	167	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
6	Fiche de révision - Méthodologie Dissertation	3	7	Guide complet pour réussir la dissertation au Bac: de l'analyse du sujet à la rédaction finale.	La méthode complète pour la dissertation.	["Analyser le sujet", "Construire la problématique", "Choisir un plan", "Rédiger l'introduction", "Construire des paragraphes argumentés", "Rédiger la conclusion"]	https://docs.winplus.cm/revisions/francais/methodo-dissert.pdf	6	medium	40	456	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
7	Fiche de révision - Figures de Style	3	7	Toutes les figures de style au programme: définition, exemples et exercices.	Reconnaître et analyser les figures de style.	["Métaphore et comparaison", "Hyperbole et litote", "Anaphore et épiphore", "Chiasme et antithèse", "Oxymore et paradoxe"]	https://docs.winplus.cm/revisions/francais/figures-style.pdf	5	facile	30	345	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
8	Revision Sheet - English Tenses	5	15	Complete review of all English tenses with usage rules and examples.	Master all English tenses in one sheet.	["Present Simple vs Continuous", "Past Simple vs Perfect", "Future tenses", "Conditional tenses", "Passive voice"]	https://docs.winplus.cm/revisions/anglais/tenses.pdf	5	medium	35	289	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
9	Revision Sheet - Common Mistakes	5	15	Most frequent errors by French speakers learning English and how to fix them.	Avoid the most common errors in English.	["False friends", "Preposition errors", "Articles (a/an/the)", "Word order", "Pronunciation traps"]	https://docs.winplus.cm/revisions/anglais/common-mistakes.pdf	5	facile	25	234	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
10	Fiche de révision - Python Essentiel	9	11	Résumé complet de Python: syntaxe, structures de données, fonctions, modules et bonnes pratiques.	Tout Python en une fiche.	["Variables et types", "Listes, tuples, dictionnaires", "Fonctions et lambda", "Modules et imports", "Gestion d'erreurs try/except"]	https://docs.winplus.cm/revisions/python/essentiel.pdf	5	facile	30	534	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
11	Fiche de révision - Algorithmes Classiques	9	11	Les algorithmes fondamentaux à connaître: tri, recherche, récursivité.	Les algorithmes incontournables.	["Tri à bulles", "Tri par sélection", "Recherche dichotomique", "Récursivité", "Complexité algorithmique"]	https://docs.winplus.cm/revisions/python/algorithmes.pdf	5	medium	40	312	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
12	Fiche de révision - React Hooks	26	11	Guide complet des React Hooks: useState, useEffect, useContext, useReducer, useMemo, useCallback.	Maîtriser tous les Hooks React.	["useState - état local", "useEffect - effets de bord", "useContext - contexte global", "useReducer - état complexe", "useMemo/useCallback - optimisation"]	https://docs.winplus.cm/revisions/react/hooks.pdf	5	difficile	45	289	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
13	Fiche de révision - Système Nerveux	8	3	Le système nerveux humain: SNC, SNP, neurones, synapses et transmission nerveuse.	Comprendre le fonctionnement du système nerveux.	["Structure du neurone", "Potentiel d'action", "Transmission synaptique", "SNC vs SNP", "Système nerveux autonome"]	https://docs.winplus.cm/revisions/bio/systeme-nerveux.pdf	5	medium	40	198	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f	\N		\N	\N	Theory	\N	\N	\N	Available	f	\N	\N	0	0	\N	\N
\.


--
-- Data for Name: Sessions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Sessions" ("Id", "Title", "Description", "StartDate", "EndDate", "MaxParticipants", "Status", "CreatedBy", "SubjectId", "CreatedAt", "UpdatedAt") FROM stdin;
1	Révision Maths - Limites et Dérivées	Session live de révision sur les limites, dérivées et continuité.	2026-02-25 14:00:00+00	2026-02-25 16:00:00+00	30	scheduled	3	1	2026-02-18 14:16:46.90276+00	\N
2	Atelier Dissertation Française	Techniques de rédaction pour le bac. Apportez vos brouillons !	2026-02-26 10:00:00+00	2026-02-26 12:00:00+00	25	scheduled	7	3	2026-02-18 14:16:46.90276+00	\N
3	Masterclass Python - Projets Pratiques	Construire un projet complet en Python de A à Z.	2026-02-28 15:00:00+00	2026-02-28 17:00:00+00	20	scheduled	11	9	2026-02-18 14:16:46.90276+00	\N
4	Conversation Anglaise - Practice Session	Session de pratique orale en anglais, niveau intermédiaire.	2026-03-01 09:00:00+00	2026-03-01 10:30:00+00	15	scheduled	15	5	2026-02-18 14:16:46.90276+00	\N
5	Biologie - Système Nerveux	Cours approfondi sur le système nerveux humain avec QCM en direct.	2026-03-03 14:00:00+00	2026-03-03 15:30:00+00	25	scheduled	3	8	2026-02-18 14:16:46.90276+00	\N
6	React Workshop - Hooks Avancés	Workshop pratique sur useReducer, useContext et hooks personnalisés.	2026-03-05 16:00:00+00	2026-03-05 18:00:00+00	20	scheduled	11	26	2026-02-18 14:16:46.90276+00	\N
\.


--
-- Data for Name: StudyGroupMembers; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."StudyGroupMembers" ("Id", "StudyGroupId", "UserId", "Role", "JoinedAt") FROM stdin;
\.


--
-- Data for Name: StudyGroups; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."StudyGroups" ("Id", "OwnerId", "Name", "Subject", "Description", "JoinCode", "IsActive", "CreatedAt", "LastActivityAt") FROM stdin;
\.


--
-- Data for Name: Subjects; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Subjects" ("Id", "Title", "Description", "Category", "ThumbnailUrl", "Price", "IsPublished", "EnrollmentCount", "AverageRating", "TotalRatings", "CreatedAt", "UpdatedAt", "IsDeleted", "IsFeatured", "Tags", "DownloadCount") FROM stdin;
1	Mathématiques Terminale S	Cours complet de maths pour la terminale scientifique	Mathématiques	https://images.unsplash.com/photo-1635070041078-e363dbe005cb?w=400	29.99	t	245	4.80	89	2025-11-10 13:08:28.636855+00	\N	f	t	["mathématiques", "limites", "dérivées", "intégrales", "terminale", "bac"]	0
2	Physique-Chimie Terminale	Maîtrisez la physique et la chimie pour le bac	Sciences	https://images.unsplash.com/photo-1532187863486-abf9dbad1b69?w=400	24.99	t	189	4.60	72	2025-11-12 13:08:28.636855+00	\N	f	t	["physique", "chimie", "mécanique", "énergie", "terminale", "bac"]	0
3	Français Dissertation	Techniques de dissertation et analyse littéraire	Lettres	https://images.unsplash.com/photo-1456513080510-7bf3a84b82f8?w=400	19.99	t	312	4.70	105	2025-11-14 13:08:28.636855+00	\N	f	t	["français", "dissertation", "commentaire", "littérature", "bac"]	0
4	Histoire-Géographie Bac	Révisions complètes HG pour le baccalauréat	Histoire	https://images.unsplash.com/photo-1524995997946-a1c2e315a42f?w=400	22.99	t	156	4.50	61	2025-11-16 13:08:28.636855+00	\N	f	f	["histoire", "géographie", "géopolitique", "bac"]	0
5	Anglais Conversationnel	Parlez anglais avec confiance et fluidité	Langues	https://images.unsplash.com/photo-1434030216411-0b793f4b6174?w=400	34.99	t	428	4.90	187	2025-11-18 13:08:28.636855+00	\N	f	t	["anglais", "conversation", "oral", "grammaire", "vocabulaire"]	0
6	Espagnol Débutant	Apprenez les bases de l'espagnol facilement	Langues	https://images.unsplash.com/photo-1551818255-e6e10975bc17?w=400	19.99	t	203	4.40	78	2025-11-20 13:08:28.636855+00	\N	f	f	["espagnol", "débutant", "langue", "grammaire"]	0
7	Philosophie Bac	Méthodes et notions clés pour l'épreuve de philo	Philosophie	https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=400	17.99	t	134	4.30	49	2025-11-22 13:08:28.636855+00	\N	f	f	["philosophie", "dissertation", "logique", "éthique", "bac"]	0
8	Biologie Humaine	Comprendre le corps humain et ses fonctions	Sciences	https://images.unsplash.com/photo-1559757148-5c350d0d3c56?w=400	27.99	t	267	4.70	93	2025-11-24 13:08:28.636855+00	\N	f	t	["biologie", "anatomie", "cellule", "système nerveux", "svt"]	0
9	Informatique Python	Programmation Python de zéro à avancé	Informatique	https://images.unsplash.com/photo-1526374965328-7f61d4dc18c5?w=400	39.99	t	534	4.90	234	2025-11-26 13:08:28.636855+00	\N	f	t	["python", "programmation", "algorithme", "code", "informatique"]	0
10	Économie & Gestion	Introduction à l'économie et gestion d'entreprise	Économie	https://images.unsplash.com/photo-1611974789855-9c2a0a7236a3?w=400	24.99	t	178	4.50	67	2025-11-28 13:08:28.636855+00	\N	f	f	["économie", "gestion", "entreprise", "marché"]	0
11	Algèbre Linéaire L1	Vecteurs, matrices et espaces vectoriels pour le supérieur	Mathématiques	https://images.unsplash.com/photo-1509228468518-180dd4864904?w=400	34.99	t	145	4.60	54	2025-11-30 13:08:28.636855+00	\N	f	f	["algèbre", "linéaire", "matrices", "espaces vectoriels", "L1"]	0
12	Chimie Organique	Réactions et mécanismes de chimie organique	Sciences	https://images.unsplash.com/photo-1628863353691-0071c8c1574d?w=400	29.99	t	112	4.40	41	2025-12-02 13:08:28.636855+00	\N	f	f	["chimie", "organique", "molécules", "réactions"]	0
13	Littérature Française	Les grands auteurs et œuvres de la littérature française	Lettres	https://images.unsplash.com/photo-1481627834876-b7833e8f5570?w=400	19.99	t	189	4.60	72	2025-12-04 13:08:28.636855+00	\N	f	f	["littérature", "française", "roman", "poésie", "théâtre"]	0
14	Géopolitique Mondiale	Comprendre les enjeux géopolitiques contemporains	Histoire	https://images.unsplash.com/photo-1521295121783-8a321d551ad2?w=400	22.99	t	98	4.30	36	2025-12-06 13:08:28.636855+00	\N	f	f	["géopolitique", "relations internationales", "conflits"]	0
16	Statistiques & Probabilités	Maîtrisez les stats et probas pour les études supérieures	Mathématiques	https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=400	29.99	t	167	4.50	62	2025-12-10 13:08:28.636855+00	\N	f	f	["statistiques", "probabilités", "loi normale", "échantillonnage"]	0
17	Éthique & Philosophie Moderne	Les grands courants philosophiques du XXe siècle	Philosophie	https://images.unsplash.com/photo-1456324504439-367cee3b3c32?w=400	17.99	t	76	4.10	28	2025-12-12 13:08:28.636855+00	\N	f	f	["éthique", "philosophie", "moderne", "morale"]	0
18	Anatomie & Physiologie	Cours d'anatomie pour étudiants en santé	Sciences	https://images.unsplash.com/photo-1530026405186-ed1f139313f8?w=400	34.99	t	223	4.70	84	2025-12-14 13:08:28.636855+00	\N	f	t	["anatomie", "physiologie", "corps humain", "organes"]	0
20	Marketing Digital	Stratégies de marketing en ligne pour entreprises	Économie	https://images.unsplash.com/photo-1432888622747-4eb9a8f5a07d?w=400	29.99	t	298	4.60	112	2025-12-18 13:08:28.636855+00	\N	f	t	["marketing", "digital", "seo", "réseaux sociaux", "stratégie"]	0
21	Calcul Intégral	Intégrales simples, doubles et applications	Mathématiques	https://images.unsplash.com/photo-1596495578065-6e0763fa1178?w=400	32.99	t	134	4.50	50	2025-12-20 13:08:28.636855+00	\N	f	f	["calcul", "intégral", "primitives", "aires"]	0
22	Thermodynamique	Lois de la thermodynamique et applications industrielles	Sciences	https://images.unsplash.com/photo-1581093588401-fbb62a02f120?w=400	27.99	t	89	4.30	33	2025-12-22 13:08:28.636855+00	\N	f	f	["thermodynamique", "chaleur", "entropie", "gaz parfait"]	0
23	Comptabilité Générale	Bases de la comptabilité pour entreprises et étudiants	Économie	https://images.unsplash.com/photo-1554224155-6726b3ff858f?w=400	24.99	t	156	4.40	58	2025-12-24 13:08:28.636855+00	\N	f	f	["comptabilité", "bilan", "résultat", "écriture comptable"]	0
25	Psychologie Cognitive	Comprendre les mécanismes de la pensée et de la mémoire	Sciences	https://images.unsplash.com/photo-1559757175-0eb30cd8c063?w=400	29.99	t	198	4.60	75	2025-12-28 13:08:28.636855+00	\N	f	f	["psychologie", "cognitive", "mémoire", "perception", "attention"]	0
26	React & Next.js	Développez des applications web modernes avec React	Informatique	https://images.unsplash.com/photo-1633356122544-f134324a6cee?w=400	49.99	t	445	4.90	198	2025-12-30 13:08:28.636855+00	\N	f	t	["react", "nextjs", "hooks", "composants", "frontend"]	0
27	Droit Civil Fondamental	Introduction au droit civil français et ses applications	Droit	https://images.unsplash.com/photo-1589829545856-d10d557cf95f?w=400	27.99	t	112	4.40	42	2026-01-01 13:08:28.636855+00	\N	f	f	["droit", "civil", "contrat", "obligations", "responsabilité"]	0
29	Dessin & Arts Plastiques	Techniques de dessin et expression artistique	Arts	https://images.unsplash.com/photo-1513364776144-60967b0f800f?w=400	22.99	t	134	4.30	49	2026-01-05 13:08:28.636855+00	\N	f	f	["dessin", "arts", "plastiques", "peinture", "créativité"]	0
30	Préparation TOEFL	Réussissez le TOEFL avec méthodes et exercices complets	Langues	https://images.unsplash.com/photo-1546410531-bb4caa6b424d?w=400	39.99	t	267	4.70	98	2026-01-07 13:08:28.636855+00	\N	f	t	["toefl", "préparation", "anglais", "test", "certification"]	0
15	Allemand Intermédiaire	Progressez en allemand niveau B1-B2	Langues	https://images.unsplash.com/photo-1467269204594-9661b134dd2b?w=400	24.99	t	87	4.20	31	2025-12-08 13:08:28.636855+00	\N	f	f	["allemand", "intermédiaire", "langue", "grammaire"]	0
19	JavaScript Avancé	Maîtrisez JS moderne, ES6+, async/await et plus	Informatique	https://images.unsplash.com/photo-1627398242454-45a1465c2479?w=400	44.99	t	389	4.80	156	2025-12-16 13:08:28.636855+00	\N	f	t	["javascript", "avancé", "async", "closures", "prototypes"]	0
24	Latin Débutant	Introduction au latin classique et à la civilisation romaine	Lettres	https://images.unsplash.com/photo-1552664730-d307ca884978?w=400	14.99	t	45	4.00	17	2025-12-26 13:08:28.636855+00	\N	f	f	["latin", "débutant", "langue", "grammaire", "déclinaisons"]	0
28	Musique & Solfège	Apprenez le solfège et les bases de la théorie musicale	Arts	https://images.unsplash.com/photo-1507838153414-b4b713384a76?w=400	19.99	t	167	4.50	63	2026-01-03 13:08:28.636855+00	\N	f	f	["musique", "solfège", "notes", "rythme", "harmonie"]	0
31	Règlementation COBAC	CONVENTION PORTANT HARMONISATION DE LA\nREGLEMENTATION BANCAIRE DANS LES ETATS DE\nL’AFRIQUE CENTRALE	\N	https://winplus-files-prod.s3.eu-north-1.amazonaws.com/covers/2026/09/8036a946ea6846abb2130dde386a59d6.png	0.00	t	0	0.00	0	2026-09-01 22:05:23.606757+00	2026-09-01 22:10:10.045585+00	f	f	[]	0
32	Mathématiques — Épreuve BAC C 2025	Épreuve officielle de mathématiques du BAC C session 2025 — Terminale C.	Mathématiques	\N	500.00	t	0	0.00	0	2026-09-01 22:30:39.667469+00	2026-09-01 22:30:39.667469+00	f	f	[]	0
\.


--
-- Data for Name: Subscriptions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Subscriptions" ("Id", "UserId", "PricingPlanId", "StartDate", "EndDate", "Status", "CreatedAt", "UpdatedAt", "RenewalCount", "IsDeleted", "IsActive", "PlanName", "TokensUsedThisMonth", "TokensResetAt") FROM stdin;
\.


--
-- Data for Name: TeacherClassStudents; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."TeacherClassStudents" ("Id", "TeacherClassId", "StudentId", "AddedAt") FROM stdin;
\.


--
-- Data for Name: TeacherClasses; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."TeacherClasses" ("Id", "TeacherId", "Name", "Level", "AcademicYear", "Description", "StudentCount", "IsActive", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: TeacherStudentLinks; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."TeacherStudentLinks" ("Id", "TeacherId", "StudentId", "Status", "InitiatedBy", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: TwoFactorTokens; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."TwoFactorTokens" ("Id", "UserId", "TotpSecret", "IsTotpEnabled", "BackupCodesCount", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: UserAIMemories; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."UserAIMemories" ("Id", "UserId", "MemoryType", "Content", "CreatedAt", "UpdatedAt") FROM stdin;
1	39	learning_preference	Préfère choisir entre révision active, exercice guidé ou fiche de synthèse	2026-09-01 08:58:53.70307+00	2026-09-01 09:04:53.941606+00
\.


--
-- Data for Name: UserInterests; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."UserInterests" ("Id", "UserId", "SubjectId", "Interest", "CreatedAt") FROM stdin;
5	2	1	Mathématiques	2026-02-20 15:01:27.530625+00
6	2	3	Français et littérature	2026-02-20 15:01:27.530625+00
7	2	5	Anglais conversationnel	2026-02-20 15:01:27.530625+00
8	2	8	Biologie	2026-02-20 15:01:27.530625+00
9	4	5	Anglais	2026-02-20 15:01:27.530625+00
10	4	20	Marketing digital	2026-02-20 15:01:27.530625+00
11	4	30	Préparation TOEFL	2026-02-20 15:01:27.530625+00
12	5	9	Suivi cours Python	2026-02-20 15:01:27.530625+00
13	5	26	Suivi cours React	2026-02-20 15:01:27.530625+00
14	13	9	Python pour concours	2026-02-20 15:01:27.530625+00
15	13	19	JavaScript avancé	2026-02-20 15:01:27.530625+00
16	13	26	React pour projets	2026-02-20 15:01:27.530625+00
\.


--
-- Data for Name: UserNotificationSettings; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."UserNotificationSettings" ("Id", "UserId", "EmailNotifications", "PushNotifications", "CourseCommunity", "Promotions", "Newsletters", "LearningReminders", "CreatedAt", "UpdatedAt") FROM stdin;
2	30	t	t	t	t	t	t	2026-08-20 07:32:50.115087	2026-08-20 07:32:50.115106
\.


--
-- Data for Name: UserPrivacySettings; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."UserPrivacySettings" ("Id", "UserId", "ProfileVisible", "ShowProgressPublic", "AllowMessages", "AllowFriends", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: UserSessions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."UserSessions" ("Id", "UserId", "DeviceName", "DeviceType", "IpAddress", "UserAgent", "Location", "RefreshTokenId", "CreatedAt", "LastActivityAt", "ExpiresAt", "IsActive") FROM stdin;
33	37	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	Cameroun	\N	2026-08-27 10:00:55.500404	2026-08-28 21:53:23.324884	2026-09-26 10:00:55.500405	t
62	37	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-08-29 17:46:03.949113	2026-08-29 17:47:26.197055	2026-09-28 17:46:03.949169	t
51	37	iPhone	mobile	127.0.0.1	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	Cameroun	\N	2026-08-28 07:51:39.071917	2026-08-28 08:24:55.225035	2026-09-27 07:51:39.071917	t
60	37	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	Cameroun	\N	2026-08-29 12:50:54.299248	2026-08-29 13:30:43.721064	2026-09-28 12:50:54.299268	t
52	36	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-08-28 08:15:28.853318	2026-08-28 19:09:39.627474	2026-09-27 08:15:28.853338	t
111	30	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-08-31 03:42:11.138183	2026-08-31 03:43:53.048671	2026-09-30 03:43:53.048671	t
54	37	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	Cameroun	\N	2026-08-28 22:03:07.624927	2026-08-28 23:13:47.14142	2026-09-27 22:03:07.624946	t
100	17	iPhone	mobile	127.0.0.1	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	Cameroun	\N	2026-08-29 19:38:53.892624	2026-08-30 19:00:36.909108	2026-09-29 19:00:36.909108	t
115	30	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	Cameroun	\N	2026-08-31 16:10:06.925567	2026-09-01 18:50:05.644247	2026-10-01 18:50:05.644247	t
63	37	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Edg/151.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-08-29 17:50:38.84577	2026-08-29 19:40:29.933971	2026-09-28 19:40:29.933971	t
53	37	iPhone	mobile	127.0.0.1	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	Cameroun	\N	2026-08-28 15:14:35.872949	2026-08-28 15:38:52.105745	2026-09-27 15:14:35.87298	t
101	37	Edge sur Windows	Windows	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0	\N	\N	2026-08-29 19:41:21.729454	2026-08-29 19:41:21.729454	2026-09-28 19:41:21.729454	t
55	37	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	Cameroun	\N	2026-08-29 08:10:03.852007	2026-08-29 09:55:27.115787	2026-09-28 08:10:03.852039	t
102	17	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0	Cameroun	\N	2026-08-29 19:41:44.40679	2026-08-29 19:41:45.458072	2026-09-28 19:41:45.458072	t
58	37	iPhone	mobile	127.0.0.1	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	Cameroun	\N	2026-08-29 10:02:41.133091	2026-08-29 10:08:15.03119	2026-09-28 10:02:41.133092	t
64	17	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-08-29 17:55:14.775592	2026-08-29 19:41:49.095986	2026-09-28 19:41:49.095986	t
59	37	iPhone	mobile	127.0.0.1	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	Cameroun	\N	2026-08-29 12:22:34.085106	2026-08-29 13:45:01.59486	2026-09-28 12:22:34.085107	t
50	37	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-08-28 07:50:21.233326	2026-08-29 13:47:53.454244	2026-09-27 07:50:21.233356	t
113	38	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	Cameroun	\N	2026-08-31 11:40:18.468727	2026-09-01 06:36:24.808022	2026-10-01 06:36:24.808022	t
61	37	iPhone	mobile	127.0.0.1	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	Cameroun	\N	2026-08-29 14:55:05.512037	2026-08-29 19:38:19.899013	2026-09-28 19:38:19.899013	t
110	30	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	Cameroun	\N	2026-08-31 03:37:04.088284	2026-08-31 04:06:02.672845	2026-09-30 04:06:02.672845	t
103	38	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	Cameroun	\N	2026-08-29 20:17:45.944383	2026-08-31 10:03:27.472665	2026-09-30 10:03:27.472665	t
57	36	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-08-29 09:45:10.289961	2026-08-31 11:39:57.417234	2026-09-30 11:39:57.417234	t
99	17	Chrome sur Windows	Windows	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	\N	\N	2026-08-29 19:25:29.023398	2026-08-29 20:17:04.231141	2026-09-28 20:17:04.231141	t
106	37	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	Cameroun	\N	2026-08-30 20:09:23.929368	2026-08-31 03:13:43.183837	2026-09-30 03:13:43.183837	t
112	38	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-08-31 10:01:24.180106	2026-08-31 12:47:39.406735	2026-09-30 12:47:39.406735	t
105	38	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	Cameroun	\N	2026-08-30 19:57:46.281939	2026-08-30 20:04:14.507578	2026-09-29 20:04:14.507578	t
114	38	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0	Cameroun	\N	2026-08-31 13:18:10.654873	2026-08-31 13:19:24.475514	2026-09-30 13:19:24.475514	t
108	30	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	Cameroun	\N	2026-08-31 03:16:27.223006	2026-08-31 03:30:44.759269	2026-09-30 03:30:44.759269	t
104	38	Chrome sur Android	Android	127.0.0.1	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	\N	\N	2026-08-29 20:18:46.74417	2026-08-30 20:15:41.041916	2026-09-29 20:15:41.041916	t
107	37	iPhone	mobile	127.0.0.1	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	Cameroun	\N	2026-08-30 20:13:01.9153	2026-08-30 20:38:41.596998	2026-09-29 20:38:41.596998	t
109	30	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	Cameroun	\N	2026-08-31 03:31:28.432778	2026-08-31 03:36:42.047411	2026-09-30 03:36:42.047411	t
119	37	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	Cameroun	\N	2026-09-01 06:36:52.638846	2026-09-01 16:03:45.378507	2026-10-01 16:03:45.378507	t
118	37	iPhone	mobile	127.0.0.1	Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Mobile/15E148 Safari/604.1	Cameroun	\N	2026-09-01 06:18:54.266561	2026-09-01 07:23:20.661962	2026-10-01 07:23:20.661962	t
124	37	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-09-01 16:34:12.610698	2026-09-01 16:34:15.09801	2026-10-01 16:34:15.09801	t
125	38	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36	Cameroun	\N	2026-09-01 16:34:50.63221	2026-09-01 16:35:55.406387	2026-10-01 16:35:55.406387	t
116	38	Edge sur Windows	Windows	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36 Edg/152.0.0.0	\N	\N	2026-08-31 19:01:28.045384	2026-09-01 16:42:15.856537	2026-10-01 16:42:15.856537	t
122	38	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Edg/152.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-09-01 13:36:42.502349	2026-09-01 16:53:10.755528	2026-10-01 16:53:10.755528	t
121	39	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-09-01 09:15:39.919433	2026-09-01 18:51:45.456208	2026-10-01 18:51:45.456208	t
120	39	Chrome sur Android	Android	127.0.0.1	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	\N	\N	2026-09-01 08:57:00.171236	2026-09-01 09:11:21.566639	2026-10-01 09:11:21.566639	t
126	17	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-09-01 18:57:58.581505	2026-09-01 18:57:59.414612	2026-10-01 18:57:59.414612	t
127	38	Android Device	mobile	127.0.0.1	Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36	Cameroun	\N	2026-09-01 19:13:17.622386	2026-09-01 19:23:16.671739	2026-10-01 19:23:16.671739	t
128	38	Windows PC	desktop	127.0.0.1	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36	Cameroun	\N	2026-09-01 21:58:10.02651	2026-09-01 22:33:51.355829	2026-10-01 22:33:51.355829	t
117	38	Edge sur Android	Android	127.0.0.1	Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Edg/152.0.0.0 Mobile Safari/537.36	\N	\N	2026-08-31 19:17:51.194146	2026-09-01 13:36:19.087614	2026-10-01 13:36:19.087614	t
123	38	Inconnu	Inconnu	127.0.0.1	curl/7.81.0	\N	\N	2026-09-01 13:37:17.540636	2026-09-01 13:37:17.540636	2026-10-01 13:37:17.540636	t
\.


--
-- Data for Name: UserTwoFactorAuthentication; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."UserTwoFactorAuthentication" ("Id", "UserId", "IsEnabled", "Method", "TotpSecret", "BackupCodes", "BackupCodesUsed", "EnabledAt", "LastVerifiedAt", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: Users; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Users" ("Id", "CognitoId", "Email", "FirstName", "LastName", "ProfileImageUrl", "Bio", "IsActive", "CreatedAt", "UpdatedAt", "IsEmailVerified", "LastLoginAt", "PasswordHash", "Phone", "Role", "VerifiedAt", "VerificationCode", "VerificationCodeExpiredAt", "AvatarUrl", "DeletedBy", "DeletedByUserId", "EmailChangeToken", "EmailChangeTokenExpiry", "PendingEmail", "IsDeleted", "EmailVerified", "Level", "City", "LastPeriodicConfirmAt", "CoverUrl", "InstitutionId") FROM stdin;
2	\N	marie.dupont@gmail.com	Marie	DUPONT	\N	Étudiante en mathématiques	t	2025-11-20 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword1	+33611111111	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Marie	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
3	\N	jean.martin@gmail.com	Jean	MARTIN	\N	Professeur de physique depuis 10 ans	t	2025-11-25 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword2	+33622222222	teacher	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Jean	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
4	\N	sophie.bernard@gmail.com	Sophie	BERNARD	\N	Passionnée de littérature française	t	2025-11-30 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword3	+33633333333	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Sophie	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
5	\N	paul.leroy@gmail.com	Paul	LEROY	\N	Parent de 2 enfants scolarisés	t	2025-12-05 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword4	+33644444444	parent	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Paul	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
6	\N	camille.moreau@gmail.com	Camille	MOREAU	\N	Lycéenne en terminale S	t	2025-12-10 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword5	+33655555555	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Camille	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
7	\N	lucas.petit@gmail.com	Lucas	PETIT	\N	Enseignant en histoire-géographie	t	2025-12-15 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword6	+33666666666	teacher	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Lucas	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
8	\N	emma.richard@gmail.com	Emma	RICHARD	\N	Étudiante en médecine L1	t	2025-12-20 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword7	+33677777777	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Emma	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
9	\N	thomas.simon@gmail.com	Thomas	SIMON	\N	Papa impliqué dans la scolarité	t	2025-12-25 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword8	+33688888888	parent	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Thomas	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
10	\N	lea.lambert@gmail.com	Léa	LAMBERT	\N	Collégienne en 3ème	t	2025-12-30 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword9	+33699999999	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Lea	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
11	\N	nicolas.garcia@gmail.com	Nicolas	GARCIA	\N	Prof de SVT et biologie	t	2026-01-04 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword10	+33610101010	teacher	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Nicolas	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
12	\N	julie.roux@gmail.com	Julie	ROUX	\N	Mère de famille, suivi scolaire	t	2026-01-09 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword11	+33621212121	parent	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Julie	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
13	\N	antoine.fournier@gmail.com	Antoine	FOURNIER	\N	Étudiant en informatique L2	t	2026-01-14 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword12	+33632323232	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Antoine	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
14	\N	clara.morel@gmail.com	Clara	MOREL	\N	Passionnée de chimie et sciences	t	2026-01-19 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword13	+33643434343	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Clara	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
15	\N	david.nguyen@gmail.com	David	NGUYEN	\N	Professeur de mathématiques sup	t	2026-01-24 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword14	+33654545454	teacher	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=David	\N	\N	\N	\N	\N	f	t	\N	\N	\N	\N	\N
19	\N	student@winplus.cm	Marie	Étudiante	\N	\N	t	2026-06-14 17:13:08.137272+00	\N	t	\N	$2a$11$gNrAFsQKBOEooFljYEZO3etTfsE08MSjHdRIcX9neNfTKnLzXljUW	\N	student	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
17	\N	admin@winplus.cm	Admin	WinPlus	\N	\N	t	2026-06-14 17:13:08.137272+00	\N	t	2026-09-01 18:57:58.600968+00	$2a$11$l6zhfP29/IPHgZqLti5pPep5/F1LnUip6M3m1GxI8idCXL9o2ixg6	\N	admin	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
23	\N	andrew22109267@gmail.com	andrew	drew449	\N	\N	t	2026-06-24 10:38:45.293699+00	\N	f	\N	$2a$11$NKt1uNq.6y23DnH6BFwCR.t4QhJUzjrQc6wkSrvyEldL8NEuuaiSy	656478741	student	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
18	\N	teacher@winplus.cm	Jean	Enseignant	\N	\N	t	2026-06-14 17:13:08.137272+00	\N	t	2026-06-22 16:05:22.927525+00	$2a$11$PH/clNnwLzsT7Ho/KF3g5exboUi7QolXhwyd1VJlkosI27JRYxOQ6	\N	teacher	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
21	\N	org@winplus.cm	Org	Partenaire	\N	\N	t	2026-06-14 17:13:08.137272+00	\N	t	2026-06-29 14:28:14.616574+00	$2a$11$kAQ1UEagAqorFrwcNt8wV.PszHDmkiBgg1BU5rqjlkzQDPxRdZIb2	\N	organization	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
25	\N	yetnaottopolycarpeavril@gmail.com	Avril	YETNA	\N	\N	t	2026-08-12 12:27:51.090785+00	\N	f	\N	$2a$11$z3g1dMSjaobV3MvOph9Lve0HgKi9ZZRMhOY7SAM8zUOiDnQoOynl2	640559201	student	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
26	\N	mko.digital.237@gmail.com	Miguel	MOUKOKO	\N	\N	t	2026-08-20 04:03:29.719809+00	\N	f	\N	$2a$11$6E6K2e8T5zg8zXxGrjIb8OS5lximlL.g8TA7QcA8bU1VWJcAEZ7/i	691697924	student	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
38	\N	anister4356@gmail.com	rodrigue	rodrigue	\N	\N	t	2026-08-29 20:16:47.312441+00	\N	t	2026-09-01 21:58:10.100948+00	$2a$11$5qRtfBCscVLuPzhJfpEaPeR9Hjd2ZYvnw76TFb/1KKfP1bRnInjZm	\N	admin	2026-08-29 20:16:47.312441+00	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
20	\N	parent@winplus.cm	Paul	Parent	\N	\N	t	2026-06-14 17:13:08.137272+00	\N	t	2026-06-23 14:47:23.75268+00	$2a$11$jg0rxy8Mvf/1feGC7mWiPOdsBO413Dwne0HxMTkvRvJacoTh4XG5O	\N	parent	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
31	\N	endaleelse@gmail.com	Else	ENDALE	\N	\N	t	2026-08-20 10:43:26.062342+00	2026-08-20 10:44:42.221337+00	t	\N	$2a$11$mw.uYlUOOexbEk.NzDoqVeWUaTYQTjH9LKDSJW6ubFtUjbWFPPQGu	671167136	student	2026-08-20 10:43:57.990881+00	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
40	\N	etudiant@test.winplus.cm	Kouam	Test	\N	\N	t	2026-09-01 22:30:39.667469+00	2026-09-01 22:30:39.667469+00	t	\N	$2a$11$l6zhfP29/IPHgZqLti5pPep5/F1LnUip6M3m1GxI8idCXL9o2ixg6	\N	student	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
41	\N	prof@test.winplus.cm	Njoya	Professeur	\N	\N	t	2026-09-01 22:30:39.667469+00	2026-09-01 22:30:39.667469+00	t	\N	$2a$11$l6zhfP29/IPHgZqLti5pPep5/F1LnUip6M3m1GxI8idCXL9o2ixg6	\N	teacher	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
36	\N	calepkengni@gmail.com	Calep	Kengni	\N	\N	t	2026-08-25 15:38:32.792551+00	2026-08-25 15:45:25.436999+00	t	2026-08-29 09:45:10.29726+00	$2a$11$GXEeVMb0CFrIC9wHzhoS0.h4ma15a5tyENeN1LOAufOrkPcxqI/rC	+237653800185	student	2026-08-25 15:44:25.316322+00	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
42	\N	parent@test.winplus.cm	Biya	Parent	\N	\N	t	2026-09-01 22:30:39.667469+00	2026-09-01 22:30:39.667469+00	t	\N	$2a$11$l6zhfP29/IPHgZqLti5pPep5/F1LnUip6M3m1GxI8idCXL9o2ixg6	\N	parent	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
39	\N	nkamfangpaolo@gmail.com	paolo	nkamfang	\N	\N	t	2026-09-01 08:56:17.525208+00	2026-09-01 08:58:20.159405+00	t	2026-09-01 09:15:39.945937+00	$2a$11$IwE60QaBp.GxMU6ayUMBjuvLVY8wn4Y9AyIKBRdo7OYWn8fkH1PXi	+237653800185	student	2026-09-01 08:56:59.569887+00	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
30	\N	miguelmoukoko4@gmail.com	Miguel	MOUKOKO	\N		t	2026-08-20 06:52:36.355855+00	2026-08-20 07:32:15.394603+00	t	2026-08-31 16:10:06.98434+00	$2a$11$jaEUixRT2j2b9m1lvt26KOuT9o27hMm6n60nal0h87O7FLNrItdCq	691697924	student	2026-08-20 06:53:43.194031+00	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
37	\N	juniorggggh@gmail.com	rodrigue	nongowe	\N	\N	t	2026-08-27 09:13:04.550203+00	2026-08-27 09:24:29.280379+00	t	2026-09-01 16:34:12.675786+00	$2a$11$PN7q832JzZsjglHjp0.plukn4SeFWNek03GDe7q1S03wPvBBVnTUC	656478741	student	2026-08-27 09:13:45.749682+00	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
43	\N	institution@test.winplus.cm	Lycée	Bilingue Test	\N	\N	t	2026-09-01 22:30:39.667469+00	2026-09-01 22:30:39.667469+00	t	\N	$2a$11$l6zhfP29/IPHgZqLti5pPep5/F1LnUip6M3m1GxI8idCXL9o2ixg6	\N	institution	\N	\N	\N	\N	\N	\N	\N	\N	\N	f	f	\N	\N	\N	\N	\N
\.


--
-- Data for Name: WebhookIdempotencyKeys; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."WebhookIdempotencyKeys" ("Id", "Reference", "ProcessedAt") FROM stdin;
\.


--
-- Data for Name: WeeklyGoals; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."WeeklyGoals" ("Id", "UserId", "WeekStart", "StudyHoursTarget", "QuizTarget", "DownloadsTarget", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: __EFMigrationsHistory; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."__EFMigrationsHistory" ("MigrationId", "ProductVersion") FROM stdin;
20251206173759_InitialCreate	8.0.0
20251208163923_AddLocalAuthFields	8.0.0
20251208231524_FixCognitoIdNullability	8.0.0
20251208233850_AddVerificationCodeField	8.0.0
20251208234435_AddVerificationCodeToUser	8.0.0
20251209000937_MakeAnalyticsUserIdNullable	8.0.0
20260203_FixUserDeletedAtField	8.0.0
20260615_AddApplicationLogs	8.0.0
20260807_AddDiscountAmountToOrders	8.0.0
20260827_AddCoverWeeklyGoalsDownloadHistory	8.0.0
20260828_AddForumThreadFollows	8.0.0
20260202_AddChatbotTables	8.0.0
\.


--
-- Data for Name: abuse_reports; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.abuse_reports (id, reported_by_user_id, reported_user_id, reported_content_id, reported_content_type, reason, description, status, action_taken, notes, resolved_at, created_at) FROM stdin;
\.


--
-- Data for Name: analytics_events; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.analytics_events (id, user_id, event_type, event_name, event_category, related_entity_type, related_entity_id, event_data, ip_address, user_agent, session_id, created_at) FROM stdin;
\.


--
-- Data for Name: badges; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.badges (id, name, description, icon_url, criteria_type, criteria_value, created_at) FROM stdin;
1	Premier Pas	Première inscription à un cours	\N	\N	\N	2025-11-10 13:08:44.030426
2	Assidu	7 jours consécutifs de connexion	\N	\N	\N	2025-11-10 13:08:44.030426
3	Expert	Compléter 10 cours avec succès	\N	\N	\N	2025-11-10 13:08:44.030426
4	Top Étudiant	Obtenir 5 étoiles sur 3 cours	\N	\N	\N	2025-11-10 13:08:44.030426
5	Ambassadeur	Parrainer 3 nouveaux utilisateurs	\N	\N	\N	2025-11-10 13:08:44.030426
\.


--
-- Data for Name: cohort_analytics; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.cohort_analytics (id, cohort_date, cohort_size, week_1_retention_percentage, week_2_retention_percentage, week_4_retention_percentage, average_rating, completion_rate, created_at) FROM stdin;
\.


--
-- Data for Name: coupons; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.coupons (id, code, description, discount_type, discount_value, min_purchase, max_uses, current_uses, applicable_courses, valid_from, valid_until, is_active, created_at) FROM stdin;
\.


--
-- Data for Name: daily_statistics; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.daily_statistics (id, stat_date, total_users, active_users, new_enrollments, completed_courses, total_revenue, total_watch_hours, created_at) FROM stdin;
\.


--
-- Data for Name: features; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.features (id, sprint_id, title, description, type, priority, status, story_points, assigned_to_user_id, created_at, updated_at) FROM stdin;
\.


--
-- Data for Name: notifications; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.notifications (id, user_id, title, message, notification_type, related_entity_type, related_entity_id, action_url, is_read, created_at, read_at) FROM stdin;
\.


--
-- Data for Name: orders; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.orders (id, user_id, order_number, total_amount, discount_amount, tax_amount, final_amount, currency, status, payment_method, payment_provider, transaction_id, invoice_url, notes, order_date, completed_date, created_at, updated_at) FROM stdin;
\.


--
-- Data for Name: refunds; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.refunds (id, order_id, user_id, reason, refund_amount, status, requested_at, processed_at, notes, created_at) FROM stdin;
\.


--
-- Data for Name: sprints; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.sprints (id, name, start_date, end_date, goal, status, created_at) FROM stdin;
\.


--
-- Data for Name: user_badges; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.user_badges (id, user_id, badge_id, earned_at) FROM stdin;
1	1	1	2026-02-18 13:10:03.525005
2	1	2	2026-02-18 13:10:03.525005
3	1	4	2026-02-18 13:10:03.525005
\.


--
-- Data for Name: user_preferences; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.user_preferences (id, user_id, notification_email, notification_push, notification_sms, theme_mode, language_ui, auto_play_videos, subtitle_preference, marketing_emails, updated_at) FROM stdin;
\.


--
-- Data for Name: user_profiles; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.user_profiles (id, user_id, role, level, learning_goal, specialization, bio_detailed, avatar_url, cover_image_url, total_hours_learning, total_completed_courses, certificates_count, rating, rating_count, is_instructor_verified, created_at, updated_at) FROM stdin;
\.


--
-- Name: AnalyticsEvents_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."AnalyticsEvents_Id_seq"', 7, true);


--
-- Name: Announcements_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Announcements_Id_seq"', 5, true);


--
-- Name: ApplicationLogs_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ApplicationLogs_Id_seq"', 17, true);


--
-- Name: BackupCodes_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."BackupCodes_Id_seq"', 1, false);


--
-- Name: CartItems_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."CartItems_Id_seq"', 5, true);


--
-- Name: Certificates_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Certificates_Id_seq"', 3, true);


--
-- Name: ChatbotContexts_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ChatbotContexts_Id_seq"', 6, true);


--
-- Name: ConcoursEvents_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ConcoursEvents_Id_seq"', 9, true);


--
-- Name: Conversations_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Conversations_Id_seq"', 12, true);


--
-- Name: CourseContents_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."CourseContents_Id_seq"', 33, true);


--
-- Name: CourseEnrollments_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."CourseEnrollments_Id_seq"', 1, false);


--
-- Name: CourseLessons_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."CourseLessons_Id_seq"', 1, false);


--
-- Name: CourseReviews_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."CourseReviews_Id_seq"', 1, false);


--
-- Name: CourseSections_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."CourseSections_Id_seq"', 1, false);


--
-- Name: Courses_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Courses_Id_seq"', 1, false);


--
-- Name: DeviceInfos_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DeviceInfos_Id_seq"', 61, true);


--
-- Name: DirectMessages_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DirectMessages_Id_seq"', 1, false);


--
-- Name: DownloadHistories_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DownloadHistories_Id_seq"', 1, false);


--
-- Name: EmailVerificationTokens_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."EmailVerificationTokens_Id_seq"', 21, true);


--
-- Name: Enrollments_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Enrollments_Id_seq"', 19, true);


--
-- Name: Events_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Events_Id_seq"', 6, true);


--
-- Name: Exams_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Exams_Id_seq"', 48, true);


--
-- Name: FavoriteCollections_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."FavoriteCollections_Id_seq"', 1, false);


--
-- Name: Favorites_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Favorites_Id_seq"', 10, true);


--
-- Name: FocusSessions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."FocusSessions_Id_seq"', 1, false);


--
-- Name: ForumModerationQueue_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ForumModerationQueue_Id_seq"', 1, false);


--
-- Name: ForumPosts_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ForumPosts_Id_seq"', 6, true);


--
-- Name: ForumThreadFollows_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ForumThreadFollows_Id_seq"', 1, true);


--
-- Name: ForumThreads_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ForumThreads_Id_seq"', 2, true);


--
-- Name: ForumVotes_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ForumVotes_Id_seq"', 1, false);


--
-- Name: Goals_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Goals_Id_seq"', 10, true);


--
-- Name: HomePageFeatures_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."HomePageFeatures_Id_seq"', 6, true);


--
-- Name: InstitutionStudents_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."InstitutionStudents_Id_seq"', 1, false);


--
-- Name: Institutions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Institutions_Id_seq"', 25, true);


--
-- Name: LearningHistories_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."LearningHistories_Id_seq"', 36, true);


--
-- Name: LessonProgress_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."LessonProgress_Id_seq"', 1, false);


--
-- Name: Levels_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Levels_Id_seq"', 24, true);


--
-- Name: Messages_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Messages_Id_seq"', 34, true);


--
-- Name: Notifications_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Notifications_Id_seq"', 11, true);


--
-- Name: OAuthAccounts_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."OAuthAccounts_Id_seq"', 1, false);


--
-- Name: OrderItems_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."OrderItems_Id_seq"', 30, true);


--
-- Name: Orders_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Orders_Id_seq"', 33, true);


--
-- Name: Pages_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Pages_Id_seq"', 5, true);


--
-- Name: ParentCreditLedgers_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ParentCreditLedgers_Id_seq"', 1, false);


--
-- Name: ParentStudentLinks_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."ParentStudentLinks_Id_seq"', 1, false);


--
-- Name: PasswordResetTokens_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."PasswordResetTokens_Id_seq"', 14, true);


--
-- Name: PlatformAnomalies_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."PlatformAnomalies_Id_seq"', 1, false);


--
-- Name: PricingPlans_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."PricingPlans_Id_seq"', 10, true);


--
-- Name: PromoCodeUsages_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."PromoCodeUsages_Id_seq"', 1, false);


--
-- Name: PromoCodes_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."PromoCodes_Id_seq"', 1, true);


--
-- Name: Promotions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Promotions_Id_seq"', 1, false);


--
-- Name: QuizAttempts_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."QuizAttempts_Id_seq"', 1, false);


--
-- Name: QuizMistakes_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."QuizMistakes_Id_seq"', 1, false);


--
-- Name: Quizzes_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Quizzes_Id_seq"', 7, true);


--
-- Name: RefreshTokens_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."RefreshTokens_Id_seq"', 122, true);


--
-- Name: Reviews_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Reviews_Id_seq"', 10, true);


--
-- Name: RevisionEnrollments_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."RevisionEnrollments_Id_seq"', 1, false);


--
-- Name: RevisionNotes_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."RevisionNotes_Id_seq"', 1, false);


--
-- Name: RevisionTags_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."RevisionTags_Id_seq"', 1, false);


--
-- Name: Revisions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Revisions_Id_seq"', 13, true);


--
-- Name: Sessions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Sessions_Id_seq"', 6, true);


--
-- Name: StudyGroupMembers_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."StudyGroupMembers_Id_seq"', 1, false);


--
-- Name: StudyGroups_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."StudyGroups_Id_seq"', 1, false);


--
-- Name: Subjects_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Subjects_Id_seq"', 32, true);


--
-- Name: Subscriptions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Subscriptions_Id_seq"', 1, true);


--
-- Name: TeacherClassStudents_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."TeacherClassStudents_Id_seq"', 1, false);


--
-- Name: TeacherClasses_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."TeacherClasses_Id_seq"', 1, false);


--
-- Name: TeacherStudentLinks_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."TeacherStudentLinks_Id_seq"', 1, false);


--
-- Name: TwoFactorTokens_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."TwoFactorTokens_Id_seq"', 1, false);


--
-- Name: UserAIMemories_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."UserAIMemories_Id_seq"', 1, true);


--
-- Name: UserInterests_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."UserInterests_Id_seq"', 16, true);


--
-- Name: UserNotificationSettings_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."UserNotificationSettings_Id_seq"', 2, true);


--
-- Name: UserPrivacySettings_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."UserPrivacySettings_Id_seq"', 1, true);


--
-- Name: UserSessions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."UserSessions_Id_seq"', 128, true);


--
-- Name: UserTwoFactorAuthentication_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."UserTwoFactorAuthentication_Id_seq"', 1, true);


--
-- Name: Users_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Users_Id_seq"', 43, true);


--
-- Name: WebhookIdempotencyKeys_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."WebhookIdempotencyKeys_Id_seq"', 1, false);


--
-- Name: WeeklyGoals_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."WeeklyGoals_Id_seq"', 1, false);


--
-- Name: abuse_reports_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.abuse_reports_id_seq', 1, false);


--
-- Name: analytics_events_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.analytics_events_id_seq', 1, false);


--
-- Name: badges_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.badges_id_seq', 1, false);


--
-- Name: cohort_analytics_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.cohort_analytics_id_seq', 1, false);


--
-- Name: coupons_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.coupons_id_seq', 1, false);


--
-- Name: daily_statistics_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.daily_statistics_id_seq', 1, false);


--
-- Name: features_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.features_id_seq', 1, false);


--
-- Name: notifications_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.notifications_id_seq', 1, false);


--
-- Name: orders_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.orders_id_seq', 1, false);


--
-- Name: payments_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."payments_Id_seq"', 28, true);


--
-- Name: refunds_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.refunds_id_seq', 1, false);


--
-- Name: sprints_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.sprints_id_seq', 1, false);


--
-- Name: user_badges_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.user_badges_id_seq', 3, true);


--
-- Name: user_preferences_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.user_preferences_id_seq', 1, false);


--
-- Name: user_profiles_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.user_profiles_id_seq', 1, false);


--
-- Name: Announcements Announcements_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Announcements"
    ADD CONSTRAINT "Announcements_pkey" PRIMARY KEY ("Id");


--
-- Name: ApplicationLogs ApplicationLogs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ApplicationLogs"
    ADD CONSTRAINT "ApplicationLogs_pkey" PRIMARY KEY ("Id");


--
-- Name: BackupCodes BackupCodes_Code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BackupCodes"
    ADD CONSTRAINT "BackupCodes_Code_key" UNIQUE ("Code");


--
-- Name: BackupCodes BackupCodes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BackupCodes"
    ADD CONSTRAINT "BackupCodes_pkey" PRIMARY KEY ("Id");


--
-- Name: Certificates Certificates_CertificateNumber_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Certificates"
    ADD CONSTRAINT "Certificates_CertificateNumber_key" UNIQUE ("CertificateNumber");


--
-- Name: Certificates Certificates_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Certificates"
    ADD CONSTRAINT "Certificates_pkey" PRIMARY KEY ("Id");


--
-- Name: ChatbotContexts ChatbotContexts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ChatbotContexts"
    ADD CONSTRAINT "ChatbotContexts_pkey" PRIMARY KEY ("Id");


--
-- Name: ConcoursEvents ConcoursEvents_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ConcoursEvents"
    ADD CONSTRAINT "ConcoursEvents_pkey" PRIMARY KEY ("Id");


--
-- Name: Conversations Conversations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Conversations"
    ADD CONSTRAINT "Conversations_pkey" PRIMARY KEY ("Id");


--
-- Name: CourseEnrollments CourseEnrollments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseEnrollments"
    ADD CONSTRAINT "CourseEnrollments_pkey" PRIMARY KEY ("Id");


--
-- Name: CourseLessons CourseLessons_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseLessons"
    ADD CONSTRAINT "CourseLessons_pkey" PRIMARY KEY ("Id");


--
-- Name: CourseReviews CourseReviews_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseReviews"
    ADD CONSTRAINT "CourseReviews_pkey" PRIMARY KEY ("Id");


--
-- Name: CourseSections CourseSections_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseSections"
    ADD CONSTRAINT "CourseSections_pkey" PRIMARY KEY ("Id");


--
-- Name: Courses Courses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Courses"
    ADD CONSTRAINT "Courses_pkey" PRIMARY KEY ("Id");


--
-- Name: DeviceInfos DeviceInfos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DeviceInfos"
    ADD CONSTRAINT "DeviceInfos_pkey" PRIMARY KEY ("Id");


--
-- Name: DirectMessages DirectMessages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DirectMessages"
    ADD CONSTRAINT "DirectMessages_pkey" PRIMARY KEY ("Id");


--
-- Name: DownloadHistories DownloadHistories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DownloadHistories"
    ADD CONSTRAINT "DownloadHistories_pkey" PRIMARY KEY ("Id");


--
-- Name: EmailVerificationTokens EmailVerificationTokens_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."EmailVerificationTokens"
    ADD CONSTRAINT "EmailVerificationTokens_pkey" PRIMARY KEY ("Id");


--
-- Name: Events Events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Events"
    ADD CONSTRAINT "Events_pkey" PRIMARY KEY ("Id");


--
-- Name: Exams Exams_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Exams"
    ADD CONSTRAINT "Exams_pkey" PRIMARY KEY ("Id");


--
-- Name: FavoriteCollections FavoriteCollections_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FavoriteCollections"
    ADD CONSTRAINT "FavoriteCollections_pkey" PRIMARY KEY ("Id");


--
-- Name: FocusSessions FocusSessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FocusSessions"
    ADD CONSTRAINT "FocusSessions_pkey" PRIMARY KEY ("Id");


--
-- Name: ForumModerationQueue ForumModerationQueue_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumModerationQueue"
    ADD CONSTRAINT "ForumModerationQueue_pkey" PRIMARY KEY ("Id");


--
-- Name: ForumPosts ForumPosts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumPosts"
    ADD CONSTRAINT "ForumPosts_pkey" PRIMARY KEY ("Id");


--
-- Name: ForumThreadFollows ForumThreadFollows_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumThreadFollows"
    ADD CONSTRAINT "ForumThreadFollows_pkey" PRIMARY KEY ("Id");


--
-- Name: ForumThreads ForumThreads_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumThreads"
    ADD CONSTRAINT "ForumThreads_pkey" PRIMARY KEY ("Id");


--
-- Name: ForumVotes ForumVotes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumVotes"
    ADD CONSTRAINT "ForumVotes_pkey" PRIMARY KEY ("Id");


--
-- Name: Goals Goals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Goals"
    ADD CONSTRAINT "Goals_pkey" PRIMARY KEY ("Id");


--
-- Name: HomePageFeatures HomePageFeatures_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."HomePageFeatures"
    ADD CONSTRAINT "HomePageFeatures_pkey" PRIMARY KEY ("Id");


--
-- Name: InstitutionStudents InstitutionStudents_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."InstitutionStudents"
    ADD CONSTRAINT "InstitutionStudents_pkey" PRIMARY KEY ("Id");


--
-- Name: Institutions Institutions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Institutions"
    ADD CONSTRAINT "Institutions_pkey" PRIMARY KEY ("Id");


--
-- Name: LessonProgress LessonProgress_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."LessonProgress"
    ADD CONSTRAINT "LessonProgress_pkey" PRIMARY KEY ("Id");


--
-- Name: Levels Levels_Name_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Levels"
    ADD CONSTRAINT "Levels_Name_key" UNIQUE ("Name");


--
-- Name: Levels Levels_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Levels"
    ADD CONSTRAINT "Levels_pkey" PRIMARY KEY ("Id");


--
-- Name: Messages Messages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Messages"
    ADD CONSTRAINT "Messages_pkey" PRIMARY KEY ("Id");


--
-- Name: OAuthAccounts OAuthAccounts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OAuthAccounts"
    ADD CONSTRAINT "OAuthAccounts_pkey" PRIMARY KEY ("Id");


--
-- Name: AnalyticsEvents PK_AnalyticsEvents; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AnalyticsEvents"
    ADD CONSTRAINT "PK_AnalyticsEvents" PRIMARY KEY ("Id");


--
-- Name: CartItems PK_CartItems; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CartItems"
    ADD CONSTRAINT "PK_CartItems" PRIMARY KEY ("Id");


--
-- Name: CourseContents PK_CourseContents; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseContents"
    ADD CONSTRAINT "PK_CourseContents" PRIMARY KEY ("Id");


--
-- Name: Enrollments PK_Enrollments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Enrollments"
    ADD CONSTRAINT "PK_Enrollments" PRIMARY KEY ("Id");


--
-- Name: Favorites PK_Favorites; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Favorites"
    ADD CONSTRAINT "PK_Favorites" PRIMARY KEY ("Id");


--
-- Name: LearningHistories PK_LearningHistories; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."LearningHistories"
    ADD CONSTRAINT "PK_LearningHistories" PRIMARY KEY ("Id");


--
-- Name: Notifications PK_Notifications; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Notifications"
    ADD CONSTRAINT "PK_Notifications" PRIMARY KEY ("Id");


--
-- Name: OrderItems PK_OrderItems; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "PK_OrderItems" PRIMARY KEY ("Id");


--
-- Name: Orders PK_Orders; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Orders"
    ADD CONSTRAINT "PK_Orders" PRIMARY KEY ("Id");


--
-- Name: Subjects PK_Subjects; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Subjects"
    ADD CONSTRAINT "PK_Subjects" PRIMARY KEY ("Id");


--
-- Name: Users PK_Users; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Users"
    ADD CONSTRAINT "PK_Users" PRIMARY KEY ("Id");


--
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");


--
-- Name: Payments PK_payments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Payments"
    ADD CONSTRAINT "PK_payments" PRIMARY KEY ("Id");


--
-- Name: Pages Pages_Slug_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Pages"
    ADD CONSTRAINT "Pages_Slug_key" UNIQUE ("Slug");


--
-- Name: Pages Pages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Pages"
    ADD CONSTRAINT "Pages_pkey" PRIMARY KEY ("Id");


--
-- Name: ParentCreditLedgers ParentCreditLedgers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ParentCreditLedgers"
    ADD CONSTRAINT "ParentCreditLedgers_pkey" PRIMARY KEY ("Id");


--
-- Name: ParentStudentLinks ParentStudentLinks_ParentId_StudentId_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ParentStudentLinks"
    ADD CONSTRAINT "ParentStudentLinks_ParentId_StudentId_key" UNIQUE ("ParentId", "StudentId");


--
-- Name: ParentStudentLinks ParentStudentLinks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ParentStudentLinks"
    ADD CONSTRAINT "ParentStudentLinks_pkey" PRIMARY KEY ("Id");


--
-- Name: PasswordResetTokens PasswordResetTokens_Token_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PasswordResetTokens"
    ADD CONSTRAINT "PasswordResetTokens_Token_key" UNIQUE ("Token");


--
-- Name: PasswordResetTokens PasswordResetTokens_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PasswordResetTokens"
    ADD CONSTRAINT "PasswordResetTokens_pkey" PRIMARY KEY ("Id");


--
-- Name: PlatformAnomalies PlatformAnomalies_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PlatformAnomalies"
    ADD CONSTRAINT "PlatformAnomalies_pkey" PRIMARY KEY ("Id");


--
-- Name: PricingPlans PricingPlans_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PricingPlans"
    ADD CONSTRAINT "PricingPlans_pkey" PRIMARY KEY ("Id");


--
-- Name: PromoCodeUsages PromoCodeUsages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodeUsages"
    ADD CONSTRAINT "PromoCodeUsages_pkey" PRIMARY KEY ("Id");


--
-- Name: PromoCodes PromoCodes_Code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodes"
    ADD CONSTRAINT "PromoCodes_Code_key" UNIQUE ("Code");


--
-- Name: PromoCodes PromoCodes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodes"
    ADD CONSTRAINT "PromoCodes_pkey" PRIMARY KEY ("Id");


--
-- Name: Promotions Promotions_Code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Promotions"
    ADD CONSTRAINT "Promotions_Code_key" UNIQUE ("Code");


--
-- Name: Promotions Promotions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Promotions"
    ADD CONSTRAINT "Promotions_pkey" PRIMARY KEY ("Id");


--
-- Name: QuizAttempts QuizAttempts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."QuizAttempts"
    ADD CONSTRAINT "QuizAttempts_pkey" PRIMARY KEY ("Id");


--
-- Name: QuizMistakes QuizMistakes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."QuizMistakes"
    ADD CONSTRAINT "QuizMistakes_pkey" PRIMARY KEY ("Id");


--
-- Name: Quizzes Quizzes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Quizzes"
    ADD CONSTRAINT "Quizzes_pkey" PRIMARY KEY ("Id");


--
-- Name: RefreshTokens RefreshTokens_Token_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RefreshTokens"
    ADD CONSTRAINT "RefreshTokens_Token_key" UNIQUE ("Token");


--
-- Name: RefreshTokens RefreshTokens_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RefreshTokens"
    ADD CONSTRAINT "RefreshTokens_pkey" PRIMARY KEY ("Id");


--
-- Name: Reviews Reviews_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Reviews"
    ADD CONSTRAINT "Reviews_pkey" PRIMARY KEY ("Id");


--
-- Name: RevisionEnrollments RevisionEnrollments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionEnrollments"
    ADD CONSTRAINT "RevisionEnrollments_pkey" PRIMARY KEY ("Id");


--
-- Name: RevisionNotes RevisionNotes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionNotes"
    ADD CONSTRAINT "RevisionNotes_pkey" PRIMARY KEY ("Id");


--
-- Name: RevisionTags RevisionTags_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionTags"
    ADD CONSTRAINT "RevisionTags_pkey" PRIMARY KEY ("Id");


--
-- Name: Revisions Revisions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Revisions"
    ADD CONSTRAINT "Revisions_pkey" PRIMARY KEY ("Id");


--
-- Name: Sessions Sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Sessions"
    ADD CONSTRAINT "Sessions_pkey" PRIMARY KEY ("Id");


--
-- Name: StudyGroupMembers StudyGroupMembers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."StudyGroupMembers"
    ADD CONSTRAINT "StudyGroupMembers_pkey" PRIMARY KEY ("Id");


--
-- Name: StudyGroups StudyGroups_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."StudyGroups"
    ADD CONSTRAINT "StudyGroups_pkey" PRIMARY KEY ("Id");


--
-- Name: Subscriptions Subscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Subscriptions"
    ADD CONSTRAINT "Subscriptions_pkey" PRIMARY KEY ("Id");


--
-- Name: TeacherClassStudents TeacherClassStudents_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherClassStudents"
    ADD CONSTRAINT "TeacherClassStudents_pkey" PRIMARY KEY ("Id");


--
-- Name: TeacherClasses TeacherClasses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherClasses"
    ADD CONSTRAINT "TeacherClasses_pkey" PRIMARY KEY ("Id");


--
-- Name: TeacherStudentLinks TeacherStudentLinks_TeacherId_StudentId_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherStudentLinks"
    ADD CONSTRAINT "TeacherStudentLinks_TeacherId_StudentId_key" UNIQUE ("TeacherId", "StudentId");


--
-- Name: TeacherStudentLinks TeacherStudentLinks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherStudentLinks"
    ADD CONSTRAINT "TeacherStudentLinks_pkey" PRIMARY KEY ("Id");


--
-- Name: TwoFactorTokens TwoFactorTokens_UserId_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TwoFactorTokens"
    ADD CONSTRAINT "TwoFactorTokens_UserId_key" UNIQUE ("UserId");


--
-- Name: TwoFactorTokens TwoFactorTokens_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TwoFactorTokens"
    ADD CONSTRAINT "TwoFactorTokens_pkey" PRIMARY KEY ("Id");


--
-- Name: Courses UQ_Courses_Slug; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Courses"
    ADD CONSTRAINT "UQ_Courses_Slug" UNIQUE ("Slug");


--
-- Name: UserNotificationSettings UQ_UserNotificationSettings_UserId; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserNotificationSettings"
    ADD CONSTRAINT "UQ_UserNotificationSettings_UserId" UNIQUE ("UserId");


--
-- Name: UserPrivacySettings UQ_UserPrivacySettings_UserId; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserPrivacySettings"
    ADD CONSTRAINT "UQ_UserPrivacySettings_UserId" UNIQUE ("UserId");


--
-- Name: UserTwoFactorAuthentication UQ_UserTwoFactorAuthentication_UserId; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserTwoFactorAuthentication"
    ADD CONSTRAINT "UQ_UserTwoFactorAuthentication_UserId" UNIQUE ("UserId");


--
-- Name: UserAIMemories UserAIMemories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserAIMemories"
    ADD CONSTRAINT "UserAIMemories_pkey" PRIMARY KEY ("Id");


--
-- Name: UserInterests UserInterests_UserId_SubjectId_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserInterests"
    ADD CONSTRAINT "UserInterests_UserId_SubjectId_key" UNIQUE ("UserId", "SubjectId");


--
-- Name: UserInterests UserInterests_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserInterests"
    ADD CONSTRAINT "UserInterests_pkey" PRIMARY KEY ("Id");


--
-- Name: UserNotificationSettings UserNotificationSettings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserNotificationSettings"
    ADD CONSTRAINT "UserNotificationSettings_pkey" PRIMARY KEY ("Id");


--
-- Name: UserPrivacySettings UserPrivacySettings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserPrivacySettings"
    ADD CONSTRAINT "UserPrivacySettings_pkey" PRIMARY KEY ("Id");


--
-- Name: UserSessions UserSessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserSessions"
    ADD CONSTRAINT "UserSessions_pkey" PRIMARY KEY ("Id");


--
-- Name: UserTwoFactorAuthentication UserTwoFactorAuthentication_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserTwoFactorAuthentication"
    ADD CONSTRAINT "UserTwoFactorAuthentication_pkey" PRIMARY KEY ("Id");


--
-- Name: WebhookIdempotencyKeys WebhookIdempotencyKeys_Reference_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."WebhookIdempotencyKeys"
    ADD CONSTRAINT "WebhookIdempotencyKeys_Reference_key" UNIQUE ("Reference");


--
-- Name: WebhookIdempotencyKeys WebhookIdempotencyKeys_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."WebhookIdempotencyKeys"
    ADD CONSTRAINT "WebhookIdempotencyKeys_pkey" PRIMARY KEY ("Id");


--
-- Name: WeeklyGoals WeeklyGoals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."WeeklyGoals"
    ADD CONSTRAINT "WeeklyGoals_pkey" PRIMARY KEY ("Id");


--
-- Name: abuse_reports abuse_reports_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.abuse_reports
    ADD CONSTRAINT abuse_reports_pkey PRIMARY KEY (id);


--
-- Name: analytics_events analytics_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_pkey PRIMARY KEY (id);


--
-- Name: badges badges_name_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.badges
    ADD CONSTRAINT badges_name_key UNIQUE (name);


--
-- Name: badges badges_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.badges
    ADD CONSTRAINT badges_pkey PRIMARY KEY (id);


--
-- Name: cohort_analytics cohort_analytics_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cohort_analytics
    ADD CONSTRAINT cohort_analytics_pkey PRIMARY KEY (id);


--
-- Name: coupons coupons_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coupons
    ADD CONSTRAINT coupons_code_key UNIQUE (code);


--
-- Name: coupons coupons_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coupons
    ADD CONSTRAINT coupons_pkey PRIMARY KEY (id);


--
-- Name: daily_statistics daily_statistics_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.daily_statistics
    ADD CONSTRAINT daily_statistics_pkey PRIMARY KEY (id);


--
-- Name: daily_statistics daily_statistics_stat_date_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.daily_statistics
    ADD CONSTRAINT daily_statistics_stat_date_key UNIQUE (stat_date);


--
-- Name: features features_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.features
    ADD CONSTRAINT features_pkey PRIMARY KEY (id);


--
-- Name: notifications notifications_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_pkey PRIMARY KEY (id);


--
-- Name: orders orders_order_number_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_order_number_key UNIQUE (order_number);


--
-- Name: orders orders_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_pkey PRIMARY KEY (id);


--
-- Name: refunds refunds_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refunds
    ADD CONSTRAINT refunds_pkey PRIMARY KEY (id);


--
-- Name: sprints sprints_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sprints
    ADD CONSTRAINT sprints_pkey PRIMARY KEY (id);


--
-- Name: ForumVotes uq_forum_votes_post_user; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumVotes"
    ADD CONSTRAINT uq_forum_votes_post_user UNIQUE ("PostId", "UserId");


--
-- Name: user_badges user_badges_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_badges
    ADD CONSTRAINT user_badges_pkey PRIMARY KEY (id);


--
-- Name: user_badges user_badges_user_id_badge_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_badges
    ADD CONSTRAINT user_badges_user_id_badge_id_key UNIQUE (user_id, badge_id);


--
-- Name: user_preferences user_preferences_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_preferences
    ADD CONSTRAINT user_preferences_pkey PRIMARY KEY (id);


--
-- Name: user_preferences user_preferences_user_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_preferences
    ADD CONSTRAINT user_preferences_user_id_key UNIQUE (user_id);


--
-- Name: user_profiles user_profiles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_profiles
    ADD CONSTRAINT user_profiles_pkey PRIMARY KEY (id);


--
-- Name: user_profiles user_profiles_user_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_profiles
    ADD CONSTRAINT user_profiles_user_id_key UNIQUE (user_id);


--
-- Name: IX_AnalyticsEvents_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_AnalyticsEvents_UserId" ON public."AnalyticsEvents" USING btree ("UserId");


--
-- Name: IX_ApplicationLogs_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ApplicationLogs_CreatedAt" ON public."ApplicationLogs" USING btree ("CreatedAt");


--
-- Name: IX_ApplicationLogs_IsResolved; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ApplicationLogs_IsResolved" ON public."ApplicationLogs" USING btree ("IsResolved");


--
-- Name: IX_ApplicationLogs_Level; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ApplicationLogs_Level" ON public."ApplicationLogs" USING btree ("Level");


--
-- Name: IX_CartItems_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CartItems_SubjectId" ON public."CartItems" USING btree ("SubjectId");


--
-- Name: IX_CartItems_UserId_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_CartItems_UserId_SubjectId" ON public."CartItems" USING btree ("UserId", "SubjectId");


--
-- Name: IX_ChatbotContexts_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_ChatbotContexts_UserId" ON public."ChatbotContexts" USING btree ("UserId");


--
-- Name: IX_ConcoursEvents_Slug_Year; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ConcoursEvents_Slug_Year" ON public."ConcoursEvents" USING btree ("Slug", "Year");


--
-- Name: IX_Conversations_IsActive; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Conversations_IsActive" ON public."Conversations" USING btree ("IsActive");


--
-- Name: IX_Conversations_IsDeleted; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Conversations_IsDeleted" ON public."Conversations" USING btree ("IsDeleted");


--
-- Name: IX_Conversations_LastMessageAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Conversations_LastMessageAt" ON public."Conversations" USING btree ("LastMessageAt");


--
-- Name: IX_Conversations_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Conversations_UserId" ON public."Conversations" USING btree ("UserId");


--
-- Name: IX_CourseContents_CreatedByUserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CourseContents_CreatedByUserId" ON public."CourseContents" USING btree ("CreatedByUserId");


--
-- Name: IX_CourseContents_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CourseContents_Status" ON public."CourseContents" USING btree ("Status");


--
-- Name: IX_CourseContents_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CourseContents_SubjectId" ON public."CourseContents" USING btree ("SubjectId");


--
-- Name: IX_CourseEnrollments_CourseId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CourseEnrollments_CourseId" ON public."CourseEnrollments" USING btree ("CourseId");


--
-- Name: IX_CourseEnrollments_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CourseEnrollments_UserId" ON public."CourseEnrollments" USING btree ("UserId");


--
-- Name: IX_CourseLessons_CourseId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CourseLessons_CourseId" ON public."CourseLessons" USING btree ("CourseId");


--
-- Name: IX_CourseLessons_SectionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CourseLessons_SectionId" ON public."CourseLessons" USING btree ("SectionId");


--
-- Name: IX_CourseReviews_CourseId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CourseReviews_CourseId" ON public."CourseReviews" USING btree ("CourseId");


--
-- Name: IX_CourseSections_CourseId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CourseSections_CourseId" ON public."CourseSections" USING btree ("CourseId");


--
-- Name: IX_Courses_Category; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Courses_Category" ON public."Courses" USING btree ("Category");


--
-- Name: IX_Courses_InstructorId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Courses_InstructorId" ON public."Courses" USING btree ("InstructorId");


--
-- Name: IX_Courses_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Courses_Status" ON public."Courses" USING btree ("Status");


--
-- Name: IX_DownloadHistories_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DownloadHistories_SubjectId" ON public."DownloadHistories" USING btree ("SubjectId");


--
-- Name: IX_DownloadHistories_UserId_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_DownloadHistories_UserId_CreatedAt" ON public."DownloadHistories" USING btree ("UserId", "CreatedAt");


--
-- Name: IX_Enrollments_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Enrollments_SubjectId" ON public."Enrollments" USING btree ("SubjectId");


--
-- Name: IX_Enrollments_UserId_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Enrollments_UserId_SubjectId" ON public."Enrollments" USING btree ("UserId", "SubjectId");


--
-- Name: IX_Exams_ExamType; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Exams_ExamType" ON public."Exams" USING btree ("ExamType");


--
-- Name: IX_Exams_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Exams_SubjectId" ON public."Exams" USING btree ("SubjectId");


--
-- Name: IX_FavoriteCollections_Name_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_FavoriteCollections_Name_UserId" ON public."FavoriteCollections" USING btree ("Name", "UserId");


--
-- Name: IX_FavoriteCollections_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_FavoriteCollections_UserId" ON public."FavoriteCollections" USING btree ("UserId");


--
-- Name: IX_Favorites_CollectionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Favorites_CollectionId" ON public."Favorites" USING btree ("CollectionId");


--
-- Name: IX_Favorites_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Favorites_SubjectId" ON public."Favorites" USING btree ("SubjectId");


--
-- Name: IX_Favorites_UserId_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Favorites_UserId_SubjectId" ON public."Favorites" USING btree ("UserId", "SubjectId");


--
-- Name: IX_FocusSessions_UserId_StartedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_FocusSessions_UserId_StartedAt" ON public."FocusSessions" USING btree ("UserId", "StartedAt");


--
-- Name: IX_ForumPosts_ThreadId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ForumPosts_ThreadId" ON public."ForumPosts" USING btree ("ThreadId");


--
-- Name: IX_ForumPosts_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ForumPosts_UserId" ON public."ForumPosts" USING btree ("UserId");


--
-- Name: IX_ForumThreadFollows_ThreadId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ForumThreadFollows_ThreadId" ON public."ForumThreadFollows" USING btree ("ThreadId");


--
-- Name: IX_ForumThreadFollows_UserId_ThreadId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_ForumThreadFollows_UserId_ThreadId" ON public."ForumThreadFollows" USING btree ("UserId", "ThreadId");


--
-- Name: IX_ForumThreads_Category; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ForumThreads_Category" ON public."ForumThreads" USING btree ("Category");


--
-- Name: IX_ForumThreads_Category_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ForumThreads_Category_CreatedAt" ON public."ForumThreads" USING btree ("Category", "CreatedAt" DESC);


--
-- Name: IX_ForumThreads_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ForumThreads_CreatedAt" ON public."ForumThreads" USING btree ("CreatedAt" DESC);


--
-- Name: IX_ForumThreads_RepliesCount; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ForumThreads_RepliesCount" ON public."ForumThreads" USING btree ("RepliesCount" DESC);


--
-- Name: IX_ForumThreads_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ForumThreads_UserId" ON public."ForumThreads" USING btree ("UserId");


--
-- Name: IX_ForumVotes_PostId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ForumVotes_PostId" ON public."ForumVotes" USING btree ("PostId");


--
-- Name: IX_InstitutionStudents_InstitutionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_InstitutionStudents_InstitutionId" ON public."InstitutionStudents" USING btree ("InstitutionId");


--
-- Name: IX_InstitutionStudents_Institution_Student; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_InstitutionStudents_Institution_Student" ON public."InstitutionStudents" USING btree ("InstitutionId", "StudentId");


--
-- Name: IX_LearningHistories_ContentId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_LearningHistories_ContentId" ON public."LearningHistories" USING btree ("ContentId");


--
-- Name: IX_LearningHistories_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_LearningHistories_SubjectId" ON public."LearningHistories" USING btree ("SubjectId");


--
-- Name: IX_LearningHistories_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_LearningHistories_UserId" ON public."LearningHistories" USING btree ("UserId");


--
-- Name: IX_LessonProgress_CourseId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_LessonProgress_CourseId" ON public."LessonProgress" USING btree ("CourseId");


--
-- Name: IX_LessonProgress_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_LessonProgress_UserId" ON public."LessonProgress" USING btree ("UserId");


--
-- Name: IX_Messages_ConversationId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Messages_ConversationId" ON public."Messages" USING btree ("ConversationId");


--
-- Name: IX_Messages_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Messages_CreatedAt" ON public."Messages" USING btree ("CreatedAt");


--
-- Name: IX_Messages_IsDeleted; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Messages_IsDeleted" ON public."Messages" USING btree ("IsDeleted");


--
-- Name: IX_Notifications_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Notifications_UserId" ON public."Notifications" USING btree ("UserId");


--
-- Name: IX_OrderItems_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_OrderItems_OrderId" ON public."OrderItems" USING btree ("OrderId");


--
-- Name: IX_Orders_OrderNumber; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Orders_OrderNumber" ON public."Orders" USING btree ("OrderNumber");


--
-- Name: IX_Orders_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Orders_UserId" ON public."Orders" USING btree ("UserId");


--
-- Name: IX_ParentCreditLedgers_Parent_Period; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ParentCreditLedgers_Parent_Period" ON public."ParentCreditLedgers" USING btree ("ParentId", "PeriodStart");


--
-- Name: IX_QuizAttempts_CompletedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_QuizAttempts_CompletedAt" ON public."QuizAttempts" USING btree ("CompletedAt" DESC);


--
-- Name: IX_QuizAttempts_QuizId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_QuizAttempts_QuizId" ON public."QuizAttempts" USING btree ("QuizId");


--
-- Name: IX_QuizAttempts_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_QuizAttempts_UserId" ON public."QuizAttempts" USING btree ("UserId");


--
-- Name: IX_QuizAttempts_User_Completed; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_QuizAttempts_User_Completed" ON public."QuizAttempts" USING btree ("UserId", "IsCompleted");


--
-- Name: IX_QuizMistakes_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_QuizMistakes_CreatedAt" ON public."QuizMistakes" USING btree ("CreatedAt");


--
-- Name: IX_QuizMistakes_UserId_IsResolved; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_QuizMistakes_UserId_IsResolved" ON public."QuizMistakes" USING btree ("UserId", "IsResolved");


--
-- Name: IX_QuizMistakes_User_Resolved; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_QuizMistakes_User_Resolved" ON public."QuizMistakes" USING btree ("UserId", "IsResolved");


--
-- Name: IX_Quizzes_ExamId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Quizzes_ExamId" ON public."Quizzes" USING btree ("ExamId");


--
-- Name: IX_Quizzes_IsPublished; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Quizzes_IsPublished" ON public."Quizzes" USING btree ("IsPublished");


--
-- Name: IX_Quizzes_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Quizzes_SubjectId" ON public."Quizzes" USING btree ("SubjectId");


--
-- Name: IX_RevisionEnrollments_RevisionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_RevisionEnrollments_RevisionId" ON public."RevisionEnrollments" USING btree ("RevisionId");


--
-- Name: IX_RevisionEnrollments_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_RevisionEnrollments_UserId" ON public."RevisionEnrollments" USING btree ("UserId");


--
-- Name: IX_RevisionEnrollments_User_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_RevisionEnrollments_User_Status" ON public."RevisionEnrollments" USING btree ("UserId", "Status");


--
-- Name: IX_RevisionNotes_User_Subject; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_RevisionNotes_User_Subject" ON public."RevisionNotes" USING btree ("UserId", "SubjectId");


--
-- Name: IX_RevisionTags_User_Subject_Label; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_RevisionTags_User_Subject_Label" ON public."RevisionTags" USING btree ("UserId", "SubjectId", "Label");


--
-- Name: IX_Revisions_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Revisions_CreatedAt" ON public."Revisions" USING btree ("CreatedAt" DESC);


--
-- Name: IX_Revisions_ExamId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Revisions_ExamId" ON public."Revisions" USING btree ("ExamId");


--
-- Name: IX_Revisions_IsDeleted; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Revisions_IsDeleted" ON public."Revisions" USING btree ("IsDeleted");


--
-- Name: IX_Revisions_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Revisions_SubjectId" ON public."Revisions" USING btree ("SubjectId");


--
-- Name: IX_StudyGroupMembers_Group_User; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_StudyGroupMembers_Group_User" ON public."StudyGroupMembers" USING btree ("StudyGroupId", "UserId");


--
-- Name: IX_StudyGroups_JoinCode; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_StudyGroups_JoinCode" ON public."StudyGroups" USING btree ("JoinCode");


--
-- Name: IX_StudyGroups_OwnerId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_StudyGroups_OwnerId" ON public."StudyGroups" USING btree ("OwnerId");


--
-- Name: IX_TeacherClassStudents_Class_Student; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_TeacherClassStudents_Class_Student" ON public."TeacherClassStudents" USING btree ("TeacherClassId", "StudentId");


--
-- Name: IX_TeacherClasses_TeacherId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_TeacherClasses_TeacherId" ON public."TeacherClasses" USING btree ("TeacherId");


--
-- Name: IX_UserNotificationSettings_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserNotificationSettings_UserId" ON public."UserNotificationSettings" USING btree ("UserId");


--
-- Name: IX_UserPrivacySettings_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserPrivacySettings_UserId" ON public."UserPrivacySettings" USING btree ("UserId");


--
-- Name: IX_UserSessions_ExpiresAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserSessions_ExpiresAt" ON public."UserSessions" USING btree ("ExpiresAt");


--
-- Name: IX_UserSessions_IsActive; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserSessions_IsActive" ON public."UserSessions" USING btree ("IsActive");


--
-- Name: IX_UserSessions_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserSessions_UserId" ON public."UserSessions" USING btree ("UserId");


--
-- Name: IX_UserTwoFactorAuthentication_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserTwoFactorAuthentication_UserId" ON public."UserTwoFactorAuthentication" USING btree ("UserId");


--
-- Name: IX_Users_CognitoId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Users_CognitoId" ON public."Users" USING btree ("CognitoId") WHERE ("CognitoId" IS NOT NULL);


--
-- Name: IX_Users_Email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Users_Email" ON public."Users" USING btree ("Email");


--
-- Name: IX_Users_InstitutionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Users_InstitutionId" ON public."Users" USING btree ("InstitutionId");


--
-- Name: IX_WeeklyGoals_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_WeeklyGoals_UserId" ON public."WeeklyGoals" USING btree ("UserId");


--
-- Name: IX_WeeklyGoals_UserId_WeekStart; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_WeeklyGoals_UserId_WeekStart" ON public."WeeklyGoals" USING btree ("UserId", "WeekStart");


--
-- Name: IX_payments_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payments_OrderId" ON public."Payments" USING btree ("OrderId");


--
-- Name: IX_payments_TransactionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payments_TransactionId" ON public."Payments" USING btree ("TransactionId");


--
-- Name: IX_payments_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payments_UserId" ON public."Payments" USING btree ("UserId");


--
-- Name: UQ_CourseEnrollments_UserId_CourseId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UQ_CourseEnrollments_UserId_CourseId" ON public."CourseEnrollments" USING btree ("UserId", "CourseId");


--
-- Name: UQ_CourseLessons_SectionId_Position; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UQ_CourseLessons_SectionId_Position" ON public."CourseLessons" USING btree ("SectionId", "Position");


--
-- Name: UQ_CourseReviews_UserId_CourseId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UQ_CourseReviews_UserId_CourseId" ON public."CourseReviews" USING btree ("UserId", "CourseId");


--
-- Name: UQ_CourseSections_CourseId_Position; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UQ_CourseSections_CourseId_Position" ON public."CourseSections" USING btree ("CourseId", "Position");


--
-- Name: UQ_LessonProgress_UserId_LessonId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "UQ_LessonProgress_UserId_LessonId" ON public."LessonProgress" USING btree ("UserId", "LessonId");


--
-- Name: idx_abuse_reports_created; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_abuse_reports_created ON public.abuse_reports USING btree (created_at);


--
-- Name: idx_abuse_reports_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_abuse_reports_status ON public.abuse_reports USING btree (status);


--
-- Name: idx_aim_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_aim_user ON public."UserAIMemories" USING btree ("UserId");


--
-- Name: idx_analytics_events_created; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_analytics_events_created ON public.analytics_events USING btree (created_at);


--
-- Name: idx_analytics_events_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_analytics_events_type ON public.analytics_events USING btree (event_type);


--
-- Name: idx_analytics_events_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_analytics_events_user ON public.analytics_events USING btree (user_id);


--
-- Name: idx_backup_codes_code; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_backup_codes_code ON public."BackupCodes" USING btree ("Code");


--
-- Name: idx_backup_codes_two_factor_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_backup_codes_two_factor_id ON public."BackupCodes" USING btree ("TwoFactorTokenId");


--
-- Name: idx_certificates_enrollment; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_certificates_enrollment ON public."Certificates" USING btree ("EnrollmentId");


--
-- Name: idx_certificates_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_certificates_user ON public."Certificates" USING btree ("UserId");


--
-- Name: idx_conversations_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_conversations_user ON public."Conversations" USING btree ("UserId");


--
-- Name: idx_coupons_code; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_coupons_code ON public.coupons USING btree (code);


--
-- Name: idx_device_infos_fingerprint; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_device_infos_fingerprint ON public."DeviceInfos" USING btree ("DeviceFingerprint");


--
-- Name: idx_device_infos_remember; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_device_infos_remember ON public."DeviceInfos" USING btree ("RememberUntil");


--
-- Name: idx_device_infos_unique; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX idx_device_infos_unique ON public."DeviceInfos" USING btree ("UserId", "DeviceFingerprint");


--
-- Name: idx_device_infos_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_device_infos_user_id ON public."DeviceInfos" USING btree ("UserId");


--
-- Name: idx_dm_from; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dm_from ON public."DirectMessages" USING btree ("FromUserId");


--
-- Name: idx_dm_to; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dm_to ON public."DirectMessages" USING btree ("ToUserId");


--
-- Name: idx_email_verification_tokens_code; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_email_verification_tokens_code ON public."EmailVerificationTokens" USING btree ("VerificationCode");


--
-- Name: idx_email_verification_tokens_expires; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_email_verification_tokens_expires ON public."EmailVerificationTokens" USING btree ("ExpiresAt");


--
-- Name: idx_email_verification_tokens_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_email_verification_tokens_user_id ON public."EmailVerificationTokens" USING btree ("UserId");


--
-- Name: idx_enroll_not_deleted; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_enroll_not_deleted ON public."Enrollments" USING btree ("IsDeleted") WHERE ("IsDeleted" = false);


--
-- Name: idx_exams_category; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_exams_category ON public."Exams" USING btree ("Category");


--
-- Name: idx_exams_subject; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_exams_subject ON public."Exams" USING btree ("SubjectId");


--
-- Name: idx_exams_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_exams_type ON public."Exams" USING btree ("ExamType");


--
-- Name: idx_exams_year; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_exams_year ON public."Exams" USING btree ("Year");


--
-- Name: idx_features_sprint; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_features_sprint ON public.features USING btree (sprint_id);


--
-- Name: idx_features_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_features_status ON public.features USING btree (status);


--
-- Name: idx_forum_posts_thread; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_forum_posts_thread ON public."ForumPosts" USING btree ("ThreadId");


--
-- Name: idx_forum_posts_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_forum_posts_user ON public."ForumPosts" USING btree ("UserId");


--
-- Name: idx_forum_threads_category; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_forum_threads_category ON public."ForumThreads" USING btree ("Category");


--
-- Name: idx_forum_threads_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_forum_threads_user ON public."ForumThreads" USING btree ("UserId");


--
-- Name: idx_goals_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_goals_user ON public."Goals" USING btree ("UserId");


--
-- Name: idx_messages_conversation; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_messages_conversation ON public."Messages" USING btree ("ConversationId");


--
-- Name: idx_notifications_read; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_notifications_read ON public.notifications USING btree (is_read);


--
-- Name: idx_notifications_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_notifications_user ON public.notifications USING btree (user_id);


--
-- Name: idx_oauth_accounts_unique; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX idx_oauth_accounts_unique ON public."OAuthAccounts" USING btree ("Provider", "ProviderUserId");


--
-- Name: idx_oauth_accounts_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_oauth_accounts_user_id ON public."OAuthAccounts" USING btree ("UserId");


--
-- Name: idx_orders_created; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_orders_created ON public.orders USING btree (order_date);


--
-- Name: idx_orders_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_orders_status ON public.orders USING btree (status);


--
-- Name: idx_orders_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_orders_user ON public.orders USING btree (user_id);


--
-- Name: idx_pages_slug; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_pages_slug ON public."Pages" USING btree ("Slug");


--
-- Name: idx_password_reset_tokens_expires; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_password_reset_tokens_expires ON public."PasswordResetTokens" USING btree ("ExpiresAt");


--
-- Name: idx_password_reset_tokens_token; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_password_reset_tokens_token ON public."PasswordResetTokens" USING btree ("Token");


--
-- Name: idx_password_reset_tokens_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_password_reset_tokens_user_id ON public."PasswordResetTokens" USING btree ("UserId");


--
-- Name: idx_payments_notchpay_ref; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_payments_notchpay_ref ON public."Payments" USING btree ("NotchpayReference");


--
-- Name: idx_payments_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_payments_status ON public."Payments" USING btree ("Status");


--
-- Name: idx_promocodes_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promocodes_active ON public."PromoCodes" USING btree ("IsActive");


--
-- Name: idx_promocodes_code; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promocodes_code ON public."PromoCodes" USING btree ("Code");


--
-- Name: idx_promocodes_validuntil; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promocodes_validuntil ON public."PromoCodes" USING btree ("ValidUntil");


--
-- Name: idx_promocodeusages_promocode; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promocodeusages_promocode ON public."PromoCodeUsages" USING btree ("PromoCodeId");


--
-- Name: idx_promocodeusages_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promocodeusages_user ON public."PromoCodeUsages" USING btree ("UserId");


--
-- Name: idx_quizzes_creator; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_quizzes_creator ON public."Quizzes" USING btree ("CreatedBy");


--
-- Name: idx_quizzes_subject; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_quizzes_subject ON public."Quizzes" USING btree ("SubjectId");


--
-- Name: idx_refresh_tokens_expires_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_refresh_tokens_expires_at ON public."RefreshTokens" USING btree ("ExpiresAt");


--
-- Name: idx_refresh_tokens_token; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_refresh_tokens_token ON public."RefreshTokens" USING btree ("Token");


--
-- Name: idx_refresh_tokens_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_refresh_tokens_user_id ON public."RefreshTokens" USING btree ("UserId");


--
-- Name: idx_revisions_creator; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_revisions_creator ON public."Revisions" USING btree ("CreatedBy");


--
-- Name: idx_revisions_subject; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_revisions_subject ON public."Revisions" USING btree ("SubjectId");


--
-- Name: idx_sprints_dates; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_sprints_dates ON public.sprints USING btree (start_date, end_date);


--
-- Name: idx_subscriptions_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_subscriptions_status ON public."Subscriptions" USING btree ("Status");


--
-- Name: idx_two_factor_tokens_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_two_factor_tokens_user_id ON public."TwoFactorTokens" USING btree ("UserId");


--
-- Name: idx_user_profiles_role; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_user_profiles_role ON public.user_profiles USING btree (role);


--
-- Name: idx_user_profiles_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_user_profiles_user_id ON public.user_profiles USING btree (user_id);


--
-- Name: idx_userinterests_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_userinterests_user ON public."UserInterests" USING btree ("UserId");


--
-- Name: ix_ForumModerationQueue_PostId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "ix_ForumModerationQueue_PostId" ON public."ForumModerationQueue" USING btree ("PostId");


--
-- Name: CourseReviews trg_course_rating; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_course_rating AFTER INSERT OR DELETE OR UPDATE ON public."CourseReviews" FOR EACH ROW EXECUTE FUNCTION public.sync_course_rating();


--
-- Name: CourseEnrollments trg_enrolled_count; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_enrolled_count AFTER INSERT OR DELETE OR UPDATE ON public."CourseEnrollments" FOR EACH ROW EXECUTE FUNCTION public.sync_enrolled_count();


--
-- Name: CourseLessons trg_lessons_count; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_lessons_count AFTER INSERT OR DELETE OR UPDATE ON public."CourseLessons" FOR EACH ROW EXECUTE FUNCTION public.sync_course_lessons_count();


--
-- Name: Courses update_courses_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER update_courses_updated_at BEFORE UPDATE ON public."Courses" FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- Name: CourseEnrollments update_enrollments_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER update_enrollments_updated_at BEFORE UPDATE ON public."CourseEnrollments" FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- Name: LessonProgress update_lesson_progress_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER update_lesson_progress_updated_at BEFORE UPDATE ON public."LessonProgress" FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- Name: CourseLessons update_lessons_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER update_lessons_updated_at BEFORE UPDATE ON public."CourseLessons" FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- Name: CourseReviews update_reviews_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER update_reviews_updated_at BEFORE UPDATE ON public."CourseReviews" FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- Name: CourseSections update_sections_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER update_sections_updated_at BEFORE UPDATE ON public."CourseSections" FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- Name: UserNotificationSettings update_usernotificationsettings_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER update_usernotificationsettings_updated_at BEFORE UPDATE ON public."UserNotificationSettings" FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- Name: UserPrivacySettings update_userprivacysettings_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER update_userprivacysettings_updated_at BEFORE UPDATE ON public."UserPrivacySettings" FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- Name: UserTwoFactorAuthentication update_usertwofactorauthentication_updated_at; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER update_usertwofactorauthentication_updated_at BEFORE UPDATE ON public."UserTwoFactorAuthentication" FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- Name: BackupCodes BackupCodes_TwoFactorTokenId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BackupCodes"
    ADD CONSTRAINT "BackupCodes_TwoFactorTokenId_fkey" FOREIGN KEY ("TwoFactorTokenId") REFERENCES public."TwoFactorTokens"("Id") ON DELETE CASCADE;


--
-- Name: Certificates Certificates_EnrollmentId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Certificates"
    ADD CONSTRAINT "Certificates_EnrollmentId_fkey" FOREIGN KEY ("EnrollmentId") REFERENCES public."Enrollments"("Id") ON DELETE CASCADE;


--
-- Name: Certificates Certificates_SubjectId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Certificates"
    ADD CONSTRAINT "Certificates_SubjectId_fkey" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE SET NULL;


--
-- Name: Certificates Certificates_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Certificates"
    ADD CONSTRAINT "Certificates_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: ChatbotContexts ChatbotContexts_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ChatbotContexts"
    ADD CONSTRAINT "ChatbotContexts_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Conversations Conversations_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Conversations"
    ADD CONSTRAINT "Conversations_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: DeviceInfos DeviceInfos_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DeviceInfos"
    ADD CONSTRAINT "DeviceInfos_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: DirectMessages DirectMessages_FromUserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DirectMessages"
    ADD CONSTRAINT "DirectMessages_FromUserId_fkey" FOREIGN KEY ("FromUserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: DirectMessages DirectMessages_ToUserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DirectMessages"
    ADD CONSTRAINT "DirectMessages_ToUserId_fkey" FOREIGN KEY ("ToUserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: EmailVerificationTokens EmailVerificationTokens_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."EmailVerificationTokens"
    ADD CONSTRAINT "EmailVerificationTokens_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Exams Exams_SubjectId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Exams"
    ADD CONSTRAINT "Exams_SubjectId_fkey" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE SET NULL;


--
-- Name: AnalyticsEvents FK_AnalyticsEvents_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."AnalyticsEvents"
    ADD CONSTRAINT "FK_AnalyticsEvents_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE SET NULL;


--
-- Name: CartItems FK_CartItems_Subjects_SubjectId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CartItems"
    ADD CONSTRAINT "FK_CartItems_Subjects_SubjectId" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE CASCADE;


--
-- Name: CartItems FK_CartItems_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CartItems"
    ADD CONSTRAINT "FK_CartItems_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: CourseContents FK_CourseContents_Subjects_SubjectId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseContents"
    ADD CONSTRAINT "FK_CourseContents_Subjects_SubjectId" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE CASCADE;


--
-- Name: CourseEnrollments FK_CourseEnrollments_Courses_CourseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseEnrollments"
    ADD CONSTRAINT "FK_CourseEnrollments_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES public."Courses"("Id") ON DELETE CASCADE;


--
-- Name: CourseEnrollments FK_CourseEnrollments_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseEnrollments"
    ADD CONSTRAINT "FK_CourseEnrollments_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: CourseLessons FK_CourseLessons_CourseSections_SectionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseLessons"
    ADD CONSTRAINT "FK_CourseLessons_CourseSections_SectionId" FOREIGN KEY ("SectionId") REFERENCES public."CourseSections"("Id") ON DELETE CASCADE;


--
-- Name: CourseLessons FK_CourseLessons_Courses_CourseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseLessons"
    ADD CONSTRAINT "FK_CourseLessons_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES public."Courses"("Id") ON DELETE CASCADE;


--
-- Name: CourseReviews FK_CourseReviews_Courses_CourseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseReviews"
    ADD CONSTRAINT "FK_CourseReviews_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES public."Courses"("Id") ON DELETE CASCADE;


--
-- Name: CourseReviews FK_CourseReviews_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseReviews"
    ADD CONSTRAINT "FK_CourseReviews_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: CourseSections FK_CourseSections_Courses_CourseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CourseSections"
    ADD CONSTRAINT "FK_CourseSections_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES public."Courses"("Id") ON DELETE CASCADE;


--
-- Name: Courses FK_Courses_Users_InstructorId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Courses"
    ADD CONSTRAINT "FK_Courses_Users_InstructorId" FOREIGN KEY ("InstructorId") REFERENCES public."Users"("Id") ON DELETE RESTRICT;


--
-- Name: DownloadHistories FK_DownloadHistories_Subjects_SubjectId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DownloadHistories"
    ADD CONSTRAINT "FK_DownloadHistories_Subjects_SubjectId" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE CASCADE;


--
-- Name: DownloadHistories FK_DownloadHistories_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DownloadHistories"
    ADD CONSTRAINT "FK_DownloadHistories_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Enrollments FK_Enrollments_Subjects_SubjectId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Enrollments"
    ADD CONSTRAINT "FK_Enrollments_Subjects_SubjectId" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE CASCADE;


--
-- Name: Enrollments FK_Enrollments_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Enrollments"
    ADD CONSTRAINT "FK_Enrollments_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Exams FK_Exams_Subjects_SubjectId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Exams"
    ADD CONSTRAINT "FK_Exams_Subjects_SubjectId" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE SET NULL;


--
-- Name: FavoriteCollections FK_FavoriteCollections_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FavoriteCollections"
    ADD CONSTRAINT "FK_FavoriteCollections_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Favorites FK_Favorites_FavoriteCollections_CollectionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Favorites"
    ADD CONSTRAINT "FK_Favorites_FavoriteCollections_CollectionId" FOREIGN KEY ("CollectionId") REFERENCES public."FavoriteCollections"("Id") ON DELETE SET NULL;


--
-- Name: Favorites FK_Favorites_Subjects_SubjectId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Favorites"
    ADD CONSTRAINT "FK_Favorites_Subjects_SubjectId" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE CASCADE;


--
-- Name: Favorites FK_Favorites_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Favorites"
    ADD CONSTRAINT "FK_Favorites_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: ForumThreadFollows FK_ForumThreadFollows_ForumThreads_ThreadId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumThreadFollows"
    ADD CONSTRAINT "FK_ForumThreadFollows_ForumThreads_ThreadId" FOREIGN KEY ("ThreadId") REFERENCES public."ForumThreads"("Id") ON DELETE CASCADE;


--
-- Name: ForumThreadFollows FK_ForumThreadFollows_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumThreadFollows"
    ADD CONSTRAINT "FK_ForumThreadFollows_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: LearningHistories FK_LearningHistories_CourseContents_ContentId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."LearningHistories"
    ADD CONSTRAINT "FK_LearningHistories_CourseContents_ContentId" FOREIGN KEY ("ContentId") REFERENCES public."CourseContents"("Id") ON DELETE SET NULL;


--
-- Name: LearningHistories FK_LearningHistories_Subjects_SubjectId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."LearningHistories"
    ADD CONSTRAINT "FK_LearningHistories_Subjects_SubjectId" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE CASCADE;


--
-- Name: LearningHistories FK_LearningHistories_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."LearningHistories"
    ADD CONSTRAINT "FK_LearningHistories_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: LessonProgress FK_LessonProgress_CourseLessons_LessonId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."LessonProgress"
    ADD CONSTRAINT "FK_LessonProgress_CourseLessons_LessonId" FOREIGN KEY ("LessonId") REFERENCES public."CourseLessons"("Id") ON DELETE CASCADE;


--
-- Name: LessonProgress FK_LessonProgress_Courses_CourseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."LessonProgress"
    ADD CONSTRAINT "FK_LessonProgress_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES public."Courses"("Id") ON DELETE CASCADE;


--
-- Name: LessonProgress FK_LessonProgress_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."LessonProgress"
    ADD CONSTRAINT "FK_LessonProgress_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Notifications FK_Notifications_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Notifications"
    ADD CONSTRAINT "FK_Notifications_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: OrderItems FK_OrderItems_Courses_CourseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "FK_OrderItems_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES public."Courses"("Id") ON DELETE SET NULL;


--
-- Name: OrderItems FK_OrderItems_Orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "FK_OrderItems_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE CASCADE;


--
-- Name: Orders FK_Orders_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Orders"
    ADD CONSTRAINT "FK_Orders_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Quizzes FK_Quizzes_Exams_ExamId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Quizzes"
    ADD CONSTRAINT "FK_Quizzes_Exams_ExamId" FOREIGN KEY ("ExamId") REFERENCES public."Exams"("Id") ON DELETE SET NULL;


--
-- Name: Quizzes FK_Quizzes_Subjects_SubjectId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Quizzes"
    ADD CONSTRAINT "FK_Quizzes_Subjects_SubjectId" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE SET NULL;


--
-- Name: RevisionEnrollments FK_RevisionEnrollments_LearningHistories; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionEnrollments"
    ADD CONSTRAINT "FK_RevisionEnrollments_LearningHistories" FOREIGN KEY ("AssociatedLearningHistoryId") REFERENCES public."LearningHistories"("Id") ON DELETE SET NULL;


--
-- Name: Revisions FK_Revisions_Exams_ExamId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Revisions"
    ADD CONSTRAINT "FK_Revisions_Exams_ExamId" FOREIGN KEY ("ExamId") REFERENCES public."Exams"("Id") ON DELETE SET NULL;


--
-- Name: Revisions FK_Revisions_Subjects_SubjectId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Revisions"
    ADD CONSTRAINT "FK_Revisions_Subjects_SubjectId" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE SET NULL;


--
-- Name: Revisions FK_Revisions_Users_CreatedByUserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Revisions"
    ADD CONSTRAINT "FK_Revisions_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES public."Users"("Id") ON DELETE SET NULL;


--
-- Name: UserNotificationSettings FK_UserNotificationSettings_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserNotificationSettings"
    ADD CONSTRAINT "FK_UserNotificationSettings_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: UserPrivacySettings FK_UserPrivacySettings_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserPrivacySettings"
    ADD CONSTRAINT "FK_UserPrivacySettings_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: UserSessions FK_UserSessions_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserSessions"
    ADD CONSTRAINT "FK_UserSessions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: UserTwoFactorAuthentication FK_UserTwoFactorAuthentication_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserTwoFactorAuthentication"
    ADD CONSTRAINT "FK_UserTwoFactorAuthentication_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Users FK_Users_Institutions_InstitutionId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Users"
    ADD CONSTRAINT "FK_Users_Institutions_InstitutionId" FOREIGN KEY ("InstitutionId") REFERENCES public."Institutions"("Id") ON DELETE SET NULL;


--
-- Name: WeeklyGoals FK_WeeklyGoals_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."WeeklyGoals"
    ADD CONSTRAINT "FK_WeeklyGoals_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Payments FK_payments_Orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Payments"
    ADD CONSTRAINT "FK_payments_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE CASCADE;


--
-- Name: Payments FK_payments_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Payments"
    ADD CONSTRAINT "FK_payments_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: FocusSessions FocusSessions_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FocusSessions"
    ADD CONSTRAINT "FocusSessions_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: ForumPosts ForumPosts_ThreadId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumPosts"
    ADD CONSTRAINT "ForumPosts_ThreadId_fkey" FOREIGN KEY ("ThreadId") REFERENCES public."ForumThreads"("Id") ON DELETE CASCADE;


--
-- Name: ForumPosts ForumPosts_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumPosts"
    ADD CONSTRAINT "ForumPosts_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: ForumThreads ForumThreads_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumThreads"
    ADD CONSTRAINT "ForumThreads_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: ForumVotes ForumVotes_PostId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumVotes"
    ADD CONSTRAINT "ForumVotes_PostId_fkey" FOREIGN KEY ("PostId") REFERENCES public."ForumPosts"("Id") ON DELETE CASCADE;


--
-- Name: ForumVotes ForumVotes_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ForumVotes"
    ADD CONSTRAINT "ForumVotes_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Goals Goals_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Goals"
    ADD CONSTRAINT "Goals_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: InstitutionStudents InstitutionStudents_InstitutionId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."InstitutionStudents"
    ADD CONSTRAINT "InstitutionStudents_InstitutionId_fkey" FOREIGN KEY ("InstitutionId") REFERENCES public."Institutions"("Id") ON DELETE CASCADE;


--
-- Name: InstitutionStudents InstitutionStudents_StudentId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."InstitutionStudents"
    ADD CONSTRAINT "InstitutionStudents_StudentId_fkey" FOREIGN KEY ("StudentId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Messages Messages_ConversationId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Messages"
    ADD CONSTRAINT "Messages_ConversationId_fkey" FOREIGN KEY ("ConversationId") REFERENCES public."Conversations"("Id") ON DELETE CASCADE;


--
-- Name: OAuthAccounts OAuthAccounts_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OAuthAccounts"
    ADD CONSTRAINT "OAuthAccounts_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Pages Pages_CreatedBy_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Pages"
    ADD CONSTRAINT "Pages_CreatedBy_fkey" FOREIGN KEY ("CreatedBy") REFERENCES public."Users"("Id") ON DELETE SET NULL;


--
-- Name: Pages Pages_UpdatedBy_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Pages"
    ADD CONSTRAINT "Pages_UpdatedBy_fkey" FOREIGN KEY ("UpdatedBy") REFERENCES public."Users"("Id") ON DELETE SET NULL;


--
-- Name: ParentCreditLedgers ParentCreditLedgers_ChildId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ParentCreditLedgers"
    ADD CONSTRAINT "ParentCreditLedgers_ChildId_fkey" FOREIGN KEY ("ChildId") REFERENCES public."Users"("Id") ON DELETE SET NULL;


--
-- Name: ParentCreditLedgers ParentCreditLedgers_ParentId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ParentCreditLedgers"
    ADD CONSTRAINT "ParentCreditLedgers_ParentId_fkey" FOREIGN KEY ("ParentId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: ParentStudentLinks ParentStudentLinks_ParentId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ParentStudentLinks"
    ADD CONSTRAINT "ParentStudentLinks_ParentId_fkey" FOREIGN KEY ("ParentId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: ParentStudentLinks ParentStudentLinks_StudentId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ParentStudentLinks"
    ADD CONSTRAINT "ParentStudentLinks_StudentId_fkey" FOREIGN KEY ("StudentId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: PasswordResetTokens PasswordResetTokens_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PasswordResetTokens"
    ADD CONSTRAINT "PasswordResetTokens_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: PromoCodeUsages PromoCodeUsages_OrderId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodeUsages"
    ADD CONSTRAINT "PromoCodeUsages_OrderId_fkey" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE CASCADE;


--
-- Name: PromoCodeUsages PromoCodeUsages_PromoCodeId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodeUsages"
    ADD CONSTRAINT "PromoCodeUsages_PromoCodeId_fkey" FOREIGN KEY ("PromoCodeId") REFERENCES public."PromoCodes"("Id") ON DELETE CASCADE;


--
-- Name: PromoCodeUsages PromoCodeUsages_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodeUsages"
    ADD CONSTRAINT "PromoCodeUsages_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: PromoCodes PromoCodes_CreatedBy_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PromoCodes"
    ADD CONSTRAINT "PromoCodes_CreatedBy_fkey" FOREIGN KEY ("CreatedBy") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: QuizAttempts QuizAttempts_QuizId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."QuizAttempts"
    ADD CONSTRAINT "QuizAttempts_QuizId_fkey" FOREIGN KEY ("QuizId") REFERENCES public."Quizzes"("Id") ON DELETE CASCADE;


--
-- Name: QuizAttempts QuizAttempts_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."QuizAttempts"
    ADD CONSTRAINT "QuizAttempts_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: QuizMistakes QuizMistakes_QuizId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."QuizMistakes"
    ADD CONSTRAINT "QuizMistakes_QuizId_fkey" FOREIGN KEY ("QuizId") REFERENCES public."Quizzes"("Id") ON DELETE SET NULL;


--
-- Name: QuizMistakes QuizMistakes_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."QuizMistakes"
    ADD CONSTRAINT "QuizMistakes_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Quizzes Quizzes_CreatedBy_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Quizzes"
    ADD CONSTRAINT "Quizzes_CreatedBy_fkey" FOREIGN KEY ("CreatedBy") REFERENCES public."Users"("Id") ON DELETE SET NULL;


--
-- Name: Quizzes Quizzes_SubjectId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Quizzes"
    ADD CONSTRAINT "Quizzes_SubjectId_fkey" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE CASCADE;


--
-- Name: RefreshTokens RefreshTokens_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RefreshTokens"
    ADD CONSTRAINT "RefreshTokens_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Reviews Reviews_SubjectId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Reviews"
    ADD CONSTRAINT "Reviews_SubjectId_fkey" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id");


--
-- Name: Reviews Reviews_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Reviews"
    ADD CONSTRAINT "Reviews_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id");


--
-- Name: RevisionEnrollments RevisionEnrollments_RevisionId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionEnrollments"
    ADD CONSTRAINT "RevisionEnrollments_RevisionId_fkey" FOREIGN KEY ("RevisionId") REFERENCES public."Revisions"("Id") ON DELETE CASCADE;


--
-- Name: RevisionEnrollments RevisionEnrollments_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionEnrollments"
    ADD CONSTRAINT "RevisionEnrollments_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: RevisionNotes RevisionNotes_SubjectId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionNotes"
    ADD CONSTRAINT "RevisionNotes_SubjectId_fkey" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE CASCADE;


--
-- Name: RevisionNotes RevisionNotes_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionNotes"
    ADD CONSTRAINT "RevisionNotes_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: RevisionTags RevisionTags_SubjectId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionTags"
    ADD CONSTRAINT "RevisionTags_SubjectId_fkey" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE CASCADE;


--
-- Name: RevisionTags RevisionTags_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."RevisionTags"
    ADD CONSTRAINT "RevisionTags_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Revisions Revisions_CreatedBy_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Revisions"
    ADD CONSTRAINT "Revisions_CreatedBy_fkey" FOREIGN KEY ("CreatedBy") REFERENCES public."Users"("Id") ON DELETE SET NULL;


--
-- Name: Revisions Revisions_SubjectId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Revisions"
    ADD CONSTRAINT "Revisions_SubjectId_fkey" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE CASCADE;


--
-- Name: StudyGroupMembers StudyGroupMembers_StudyGroupId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."StudyGroupMembers"
    ADD CONSTRAINT "StudyGroupMembers_StudyGroupId_fkey" FOREIGN KEY ("StudyGroupId") REFERENCES public."StudyGroups"("Id") ON DELETE CASCADE;


--
-- Name: StudyGroupMembers StudyGroupMembers_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."StudyGroupMembers"
    ADD CONSTRAINT "StudyGroupMembers_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: StudyGroups StudyGroups_OwnerId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."StudyGroups"
    ADD CONSTRAINT "StudyGroups_OwnerId_fkey" FOREIGN KEY ("OwnerId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Subscriptions Subscriptions_PricingPlanId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Subscriptions"
    ADD CONSTRAINT "Subscriptions_PricingPlanId_fkey" FOREIGN KEY ("PricingPlanId") REFERENCES public."PricingPlans"("Id");


--
-- Name: Subscriptions Subscriptions_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Subscriptions"
    ADD CONSTRAINT "Subscriptions_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id");


--
-- Name: TeacherClassStudents TeacherClassStudents_StudentId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherClassStudents"
    ADD CONSTRAINT "TeacherClassStudents_StudentId_fkey" FOREIGN KEY ("StudentId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: TeacherClassStudents TeacherClassStudents_TeacherClassId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherClassStudents"
    ADD CONSTRAINT "TeacherClassStudents_TeacherClassId_fkey" FOREIGN KEY ("TeacherClassId") REFERENCES public."TeacherClasses"("Id") ON DELETE CASCADE;


--
-- Name: TeacherClasses TeacherClasses_TeacherId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherClasses"
    ADD CONSTRAINT "TeacherClasses_TeacherId_fkey" FOREIGN KEY ("TeacherId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: TeacherStudentLinks TeacherStudentLinks_InitiatedBy_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherStudentLinks"
    ADD CONSTRAINT "TeacherStudentLinks_InitiatedBy_fkey" FOREIGN KEY ("InitiatedBy") REFERENCES public."Users"("Id");


--
-- Name: TeacherStudentLinks TeacherStudentLinks_StudentId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherStudentLinks"
    ADD CONSTRAINT "TeacherStudentLinks_StudentId_fkey" FOREIGN KEY ("StudentId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: TeacherStudentLinks TeacherStudentLinks_TeacherId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TeacherStudentLinks"
    ADD CONSTRAINT "TeacherStudentLinks_TeacherId_fkey" FOREIGN KEY ("TeacherId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: TwoFactorTokens TwoFactorTokens_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TwoFactorTokens"
    ADD CONSTRAINT "TwoFactorTokens_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: UserAIMemories UserAIMemories_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserAIMemories"
    ADD CONSTRAINT "UserAIMemories_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: UserInterests UserInterests_SubjectId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserInterests"
    ADD CONSTRAINT "UserInterests_SubjectId_fkey" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id") ON DELETE CASCADE;


--
-- Name: UserInterests UserInterests_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserInterests"
    ADD CONSTRAINT "UserInterests_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: features features_sprint_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.features
    ADD CONSTRAINT features_sprint_id_fkey FOREIGN KEY (sprint_id) REFERENCES public.sprints(id) ON DELETE SET NULL;


--
-- Name: refunds refunds_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refunds
    ADD CONSTRAINT refunds_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(id) ON DELETE CASCADE;


--
-- Name: user_badges user_badges_badge_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_badges
    ADD CONSTRAINT user_badges_badge_id_fkey FOREIGN KEY (badge_id) REFERENCES public.badges(id) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict X0GVjgmpUNc5L8JmARqIn6ra5u1dwjshBLqcXIRUs1ksoWJaMiCdFV8He50hNaC

