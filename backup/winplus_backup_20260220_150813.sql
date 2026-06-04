--
-- PostgreSQL database dump
--

\restrict HL3bY2EiZcN7BH3bgyfcfnfsbcaDAxMkjSwIaFtrGCnQaDGwWN2C6kYTvMJu99Y

-- Dumped from database version 17.7 (Ubuntu 17.7-3.pgdg24.04+1)
-- Dumped by pg_dump version 18.1 (Ubuntu 18.1-1.pgdg24.04+2)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
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
ALTER TABLE IF EXISTS ONLY public."TwoFactorTokens" DROP CONSTRAINT IF EXISTS "TwoFactorTokens_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Subscriptions" DROP CONSTRAINT IF EXISTS "Subscriptions_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Subscriptions" DROP CONSTRAINT IF EXISTS "Subscriptions_PricingPlanId_fkey";
ALTER TABLE IF EXISTS ONLY public."Revisions" DROP CONSTRAINT IF EXISTS "Revisions_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."Revisions" DROP CONSTRAINT IF EXISTS "Revisions_CreatedBy_fkey";
ALTER TABLE IF EXISTS ONLY public."Reviews" DROP CONSTRAINT IF EXISTS "Reviews_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Reviews" DROP CONSTRAINT IF EXISTS "Reviews_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."RefreshTokens" DROP CONSTRAINT IF EXISTS "RefreshTokens_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Quizzes" DROP CONSTRAINT IF EXISTS "Quizzes_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."Quizzes" DROP CONSTRAINT IF EXISTS "Quizzes_CreatedBy_fkey";
ALTER TABLE IF EXISTS ONLY public."PasswordResetTokens" DROP CONSTRAINT IF EXISTS "PasswordResetTokens_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Pages" DROP CONSTRAINT IF EXISTS "Pages_UpdatedBy_fkey";
ALTER TABLE IF EXISTS ONLY public."Pages" DROP CONSTRAINT IF EXISTS "Pages_CreatedBy_fkey";
ALTER TABLE IF EXISTS ONLY public."OAuthAccounts" DROP CONSTRAINT IF EXISTS "OAuthAccounts_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Goals" DROP CONSTRAINT IF EXISTS "Goals_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public.payments DROP CONSTRAINT IF EXISTS "FK_payments_Users_UserId";
ALTER TABLE IF EXISTS ONLY public.payments DROP CONSTRAINT IF EXISTS "FK_payments_Orders_OrderId";
ALTER TABLE IF EXISTS ONLY public."UserTwoFactorAuthentication" DROP CONSTRAINT IF EXISTS "FK_UserTwoFactorAuthentication_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."UserSessions" DROP CONSTRAINT IF EXISTS "FK_UserSessions_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."UserPrivacySettings" DROP CONSTRAINT IF EXISTS "FK_UserPrivacySettings_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."UserNotificationSettings" DROP CONSTRAINT IF EXISTS "FK_UserNotificationSettings_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."Orders" DROP CONSTRAINT IF EXISTS "FK_Orders_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."OrderItems" DROP CONSTRAINT IF EXISTS "FK_OrderItems_Orders_OrderId";
ALTER TABLE IF EXISTS ONLY public."Notifications" DROP CONSTRAINT IF EXISTS "FK_Notifications_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."LearningHistories" DROP CONSTRAINT IF EXISTS "FK_LearningHistories_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."LearningHistories" DROP CONSTRAINT IF EXISTS "FK_LearningHistories_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."LearningHistories" DROP CONSTRAINT IF EXISTS "FK_LearningHistories_CourseContents_ContentId";
ALTER TABLE IF EXISTS ONLY public."Favorites" DROP CONSTRAINT IF EXISTS "FK_Favorites_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."Favorites" DROP CONSTRAINT IF EXISTS "FK_Favorites_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."Enrollments" DROP CONSTRAINT IF EXISTS "FK_Enrollments_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."Enrollments" DROP CONSTRAINT IF EXISTS "FK_Enrollments_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."CourseContents" DROP CONSTRAINT IF EXISTS "FK_CourseContents_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."CartItems" DROP CONSTRAINT IF EXISTS "FK_CartItems_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."CartItems" DROP CONSTRAINT IF EXISTS "FK_CartItems_Subjects_SubjectId";
ALTER TABLE IF EXISTS ONLY public."AnalyticsEvents" DROP CONSTRAINT IF EXISTS "FK_AnalyticsEvents_Users_UserId";
ALTER TABLE IF EXISTS ONLY public."Exams" DROP CONSTRAINT IF EXISTS "Exams_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."EmailVerificationTokens" DROP CONSTRAINT IF EXISTS "EmailVerificationTokens_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."DeviceInfos" DROP CONSTRAINT IF EXISTS "DeviceInfos_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Certificates" DROP CONSTRAINT IF EXISTS "Certificates_UserId_fkey";
ALTER TABLE IF EXISTS ONLY public."Certificates" DROP CONSTRAINT IF EXISTS "Certificates_SubjectId_fkey";
ALTER TABLE IF EXISTS ONLY public."Certificates" DROP CONSTRAINT IF EXISTS "Certificates_EnrollmentId_fkey";
ALTER TABLE IF EXISTS ONLY public."BackupCodes" DROP CONSTRAINT IF EXISTS "BackupCodes_TwoFactorTokenId_fkey";
DROP TRIGGER IF EXISTS update_usertwofactorauthentication_updated_at ON public."UserTwoFactorAuthentication";
DROP TRIGGER IF EXISTS update_userprivacysettings_updated_at ON public."UserPrivacySettings";
DROP TRIGGER IF EXISTS update_usernotificationsettings_updated_at ON public."UserNotificationSettings";
DROP INDEX IF EXISTS public.idx_userinterests_user;
DROP INDEX IF EXISTS public.idx_user_profiles_user_id;
DROP INDEX IF EXISTS public.idx_user_profiles_role;
DROP INDEX IF EXISTS public.idx_two_factor_tokens_user_id;
DROP INDEX IF EXISTS public.idx_sprints_dates;
DROP INDEX IF EXISTS public.idx_revisions_subject;
DROP INDEX IF EXISTS public.idx_revisions_creator;
DROP INDEX IF EXISTS public.idx_refresh_tokens_user_id;
DROP INDEX IF EXISTS public.idx_refresh_tokens_token;
DROP INDEX IF EXISTS public.idx_refresh_tokens_expires_at;
DROP INDEX IF EXISTS public.idx_quizzes_subject;
DROP INDEX IF EXISTS public.idx_quizzes_creator;
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
DROP INDEX IF EXISTS public.idx_goals_user;
DROP INDEX IF EXISTS public.idx_features_status;
DROP INDEX IF EXISTS public.idx_features_sprint;
DROP INDEX IF EXISTS public.idx_exams_year;
DROP INDEX IF EXISTS public.idx_exams_type;
DROP INDEX IF EXISTS public.idx_exams_subject;
DROP INDEX IF EXISTS public.idx_exams_category;
DROP INDEX IF EXISTS public.idx_email_verification_tokens_user_id;
DROP INDEX IF EXISTS public.idx_email_verification_tokens_expires;
DROP INDEX IF EXISTS public.idx_email_verification_tokens_code;
DROP INDEX IF EXISTS public.idx_device_infos_user_id;
DROP INDEX IF EXISTS public.idx_device_infos_unique;
DROP INDEX IF EXISTS public.idx_device_infos_remember;
DROP INDEX IF EXISTS public.idx_device_infos_fingerprint;
DROP INDEX IF EXISTS public.idx_coupons_code;
DROP INDEX IF EXISTS public.idx_certificates_user;
DROP INDEX IF EXISTS public.idx_certificates_enrollment;
DROP INDEX IF EXISTS public.idx_backup_codes_two_factor_id;
DROP INDEX IF EXISTS public.idx_backup_codes_code;
DROP INDEX IF EXISTS public.idx_analytics_events_user;
DROP INDEX IF EXISTS public.idx_analytics_events_type;
DROP INDEX IF EXISTS public.idx_analytics_events_created;
DROP INDEX IF EXISTS public.idx_abuse_reports_status;
DROP INDEX IF EXISTS public.idx_abuse_reports_created;
DROP INDEX IF EXISTS public."IX_payments_UserId";
DROP INDEX IF EXISTS public."IX_payments_TransactionId";
DROP INDEX IF EXISTS public."IX_payments_OrderId";
DROP INDEX IF EXISTS public."IX_Users_Email";
DROP INDEX IF EXISTS public."IX_Users_CognitoId";
DROP INDEX IF EXISTS public."IX_UserTwoFactorAuthentication_UserId";
DROP INDEX IF EXISTS public."IX_UserSessions_UserId";
DROP INDEX IF EXISTS public."IX_UserSessions_IsActive";
DROP INDEX IF EXISTS public."IX_UserSessions_ExpiresAt";
DROP INDEX IF EXISTS public."IX_UserPrivacySettings_UserId";
DROP INDEX IF EXISTS public."IX_UserNotificationSettings_UserId";
DROP INDEX IF EXISTS public."IX_Orders_UserId";
DROP INDEX IF EXISTS public."IX_Orders_OrderNumber";
DROP INDEX IF EXISTS public."IX_OrderItems_OrderId";
DROP INDEX IF EXISTS public."IX_Notifications_UserId";
DROP INDEX IF EXISTS public."IX_LearningHistories_UserId";
DROP INDEX IF EXISTS public."IX_LearningHistories_SubjectId";
DROP INDEX IF EXISTS public."IX_LearningHistories_ContentId";
DROP INDEX IF EXISTS public."IX_Favorites_UserId_SubjectId";
DROP INDEX IF EXISTS public."IX_Favorites_SubjectId";
DROP INDEX IF EXISTS public."IX_Enrollments_UserId_SubjectId";
DROP INDEX IF EXISTS public."IX_Enrollments_SubjectId";
DROP INDEX IF EXISTS public."IX_CourseContents_SubjectId";
DROP INDEX IF EXISTS public."IX_CartItems_UserId_SubjectId";
DROP INDEX IF EXISTS public."IX_CartItems_SubjectId";
DROP INDEX IF EXISTS public."IX_AnalyticsEvents_UserId";
ALTER TABLE IF EXISTS ONLY public.user_profiles DROP CONSTRAINT IF EXISTS user_profiles_user_id_key;
ALTER TABLE IF EXISTS ONLY public.user_profiles DROP CONSTRAINT IF EXISTS user_profiles_pkey;
ALTER TABLE IF EXISTS ONLY public.user_preferences DROP CONSTRAINT IF EXISTS user_preferences_user_id_key;
ALTER TABLE IF EXISTS ONLY public.user_preferences DROP CONSTRAINT IF EXISTS user_preferences_pkey;
ALTER TABLE IF EXISTS ONLY public.user_badges DROP CONSTRAINT IF EXISTS user_badges_user_id_badge_id_key;
ALTER TABLE IF EXISTS ONLY public.user_badges DROP CONSTRAINT IF EXISTS user_badges_pkey;
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
ALTER TABLE IF EXISTS ONLY public."UserTwoFactorAuthentication" DROP CONSTRAINT IF EXISTS "UserTwoFactorAuthentication_pkey";
ALTER TABLE IF EXISTS ONLY public."UserSessions" DROP CONSTRAINT IF EXISTS "UserSessions_pkey";
ALTER TABLE IF EXISTS ONLY public."UserPrivacySettings" DROP CONSTRAINT IF EXISTS "UserPrivacySettings_pkey";
ALTER TABLE IF EXISTS ONLY public."UserNotificationSettings" DROP CONSTRAINT IF EXISTS "UserNotificationSettings_pkey";
ALTER TABLE IF EXISTS ONLY public."UserInterests" DROP CONSTRAINT IF EXISTS "UserInterests_pkey";
ALTER TABLE IF EXISTS ONLY public."UserInterests" DROP CONSTRAINT IF EXISTS "UserInterests_UserId_SubjectId_key";
ALTER TABLE IF EXISTS ONLY public."UserTwoFactorAuthentication" DROP CONSTRAINT IF EXISTS "UQ_UserTwoFactorAuthentication_UserId";
ALTER TABLE IF EXISTS ONLY public."UserPrivacySettings" DROP CONSTRAINT IF EXISTS "UQ_UserPrivacySettings_UserId";
ALTER TABLE IF EXISTS ONLY public."UserNotificationSettings" DROP CONSTRAINT IF EXISTS "UQ_UserNotificationSettings_UserId";
ALTER TABLE IF EXISTS ONLY public."TwoFactorTokens" DROP CONSTRAINT IF EXISTS "TwoFactorTokens_pkey";
ALTER TABLE IF EXISTS ONLY public."TwoFactorTokens" DROP CONSTRAINT IF EXISTS "TwoFactorTokens_UserId_key";
ALTER TABLE IF EXISTS ONLY public."Subscriptions" DROP CONSTRAINT IF EXISTS "Subscriptions_pkey";
ALTER TABLE IF EXISTS ONLY public."Sessions" DROP CONSTRAINT IF EXISTS "Sessions_pkey";
ALTER TABLE IF EXISTS ONLY public."Revisions" DROP CONSTRAINT IF EXISTS "Revisions_pkey";
ALTER TABLE IF EXISTS ONLY public."Reviews" DROP CONSTRAINT IF EXISTS "Reviews_pkey";
ALTER TABLE IF EXISTS ONLY public."RefreshTokens" DROP CONSTRAINT IF EXISTS "RefreshTokens_pkey";
ALTER TABLE IF EXISTS ONLY public."RefreshTokens" DROP CONSTRAINT IF EXISTS "RefreshTokens_Token_key";
ALTER TABLE IF EXISTS ONLY public."Quizzes" DROP CONSTRAINT IF EXISTS "Quizzes_pkey";
ALTER TABLE IF EXISTS ONLY public."PricingPlans" DROP CONSTRAINT IF EXISTS "PricingPlans_pkey";
ALTER TABLE IF EXISTS ONLY public."PasswordResetTokens" DROP CONSTRAINT IF EXISTS "PasswordResetTokens_pkey";
ALTER TABLE IF EXISTS ONLY public."PasswordResetTokens" DROP CONSTRAINT IF EXISTS "PasswordResetTokens_Token_key";
ALTER TABLE IF EXISTS ONLY public."Pages" DROP CONSTRAINT IF EXISTS "Pages_pkey";
ALTER TABLE IF EXISTS ONLY public."Pages" DROP CONSTRAINT IF EXISTS "Pages_Slug_key";
ALTER TABLE IF EXISTS ONLY public.payments DROP CONSTRAINT IF EXISTS "PK_payments";
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
ALTER TABLE IF EXISTS ONLY public."Levels" DROP CONSTRAINT IF EXISTS "Levels_pkey";
ALTER TABLE IF EXISTS ONLY public."Levels" DROP CONSTRAINT IF EXISTS "Levels_Name_key";
ALTER TABLE IF EXISTS ONLY public."Institutions" DROP CONSTRAINT IF EXISTS "Institutions_pkey";
ALTER TABLE IF EXISTS ONLY public."HomePageFeatures" DROP CONSTRAINT IF EXISTS "HomePageFeatures_pkey";
ALTER TABLE IF EXISTS ONLY public."Goals" DROP CONSTRAINT IF EXISTS "Goals_pkey";
ALTER TABLE IF EXISTS ONLY public."Exams" DROP CONSTRAINT IF EXISTS "Exams_pkey";
ALTER TABLE IF EXISTS ONLY public."Events" DROP CONSTRAINT IF EXISTS "Events_pkey";
ALTER TABLE IF EXISTS ONLY public."EmailVerificationTokens" DROP CONSTRAINT IF EXISTS "EmailVerificationTokens_pkey";
ALTER TABLE IF EXISTS ONLY public."DeviceInfos" DROP CONSTRAINT IF EXISTS "DeviceInfos_pkey";
ALTER TABLE IF EXISTS ONLY public."Certificates" DROP CONSTRAINT IF EXISTS "Certificates_pkey";
ALTER TABLE IF EXISTS ONLY public."Certificates" DROP CONSTRAINT IF EXISTS "Certificates_CertificateNumber_key";
ALTER TABLE IF EXISTS ONLY public."BackupCodes" DROP CONSTRAINT IF EXISTS "BackupCodes_pkey";
ALTER TABLE IF EXISTS ONLY public."BackupCodes" DROP CONSTRAINT IF EXISTS "BackupCodes_Code_key";
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
ALTER TABLE IF EXISTS public."UserTwoFactorAuthentication" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."UserSessions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."UserPrivacySettings" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."UserNotificationSettings" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."UserInterests" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."TwoFactorTokens" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Subscriptions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Sessions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Revisions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Reviews" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."RefreshTokens" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Quizzes" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."PricingPlans" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."PasswordResetTokens" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Pages" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."OAuthAccounts" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Levels" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Institutions" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."HomePageFeatures" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Goals" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Exams" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Events" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."EmailVerificationTokens" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."DeviceInfos" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."Certificates" ALTER COLUMN "Id" DROP DEFAULT;
ALTER TABLE IF EXISTS public."BackupCodes" ALTER COLUMN "Id" DROP DEFAULT;
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
DROP TABLE IF EXISTS public.payments;
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
DROP SEQUENCE IF EXISTS public."TwoFactorTokens_Id_seq";
DROP TABLE IF EXISTS public."TwoFactorTokens";
DROP SEQUENCE IF EXISTS public."Subscriptions_Id_seq";
DROP TABLE IF EXISTS public."Subscriptions";
DROP TABLE IF EXISTS public."Subjects";
DROP SEQUENCE IF EXISTS public."Sessions_Id_seq";
DROP TABLE IF EXISTS public."Sessions";
DROP SEQUENCE IF EXISTS public."Revisions_Id_seq";
DROP TABLE IF EXISTS public."Revisions";
DROP SEQUENCE IF EXISTS public."Reviews_Id_seq";
DROP TABLE IF EXISTS public."Reviews";
DROP SEQUENCE IF EXISTS public."RefreshTokens_Id_seq";
DROP TABLE IF EXISTS public."RefreshTokens";
DROP SEQUENCE IF EXISTS public."Quizzes_Id_seq";
DROP TABLE IF EXISTS public."Quizzes";
DROP SEQUENCE IF EXISTS public."PricingPlans_Id_seq";
DROP TABLE IF EXISTS public."PricingPlans";
DROP SEQUENCE IF EXISTS public."PasswordResetTokens_Id_seq";
DROP TABLE IF EXISTS public."PasswordResetTokens";
DROP SEQUENCE IF EXISTS public."Pages_Id_seq";
DROP TABLE IF EXISTS public."Pages";
DROP TABLE IF EXISTS public."Orders";
DROP TABLE IF EXISTS public."OrderItems";
DROP SEQUENCE IF EXISTS public."OAuthAccounts_Id_seq";
DROP TABLE IF EXISTS public."OAuthAccounts";
DROP TABLE IF EXISTS public."Notifications";
DROP SEQUENCE IF EXISTS public."Levels_Id_seq";
DROP TABLE IF EXISTS public."Levels";
DROP TABLE IF EXISTS public."LearningHistories";
DROP SEQUENCE IF EXISTS public."Institutions_Id_seq";
DROP TABLE IF EXISTS public."Institutions";
DROP SEQUENCE IF EXISTS public."HomePageFeatures_Id_seq";
DROP TABLE IF EXISTS public."HomePageFeatures";
DROP SEQUENCE IF EXISTS public."Goals_Id_seq";
DROP TABLE IF EXISTS public."Goals";
DROP TABLE IF EXISTS public."Favorites";
DROP SEQUENCE IF EXISTS public."Exams_Id_seq";
DROP TABLE IF EXISTS public."Exams";
DROP SEQUENCE IF EXISTS public."Events_Id_seq";
DROP TABLE IF EXISTS public."Events";
DROP TABLE IF EXISTS public."Enrollments";
DROP SEQUENCE IF EXISTS public."EmailVerificationTokens_Id_seq";
DROP TABLE IF EXISTS public."EmailVerificationTokens";
DROP SEQUENCE IF EXISTS public."DeviceInfos_Id_seq";
DROP TABLE IF EXISTS public."DeviceInfos";
DROP TABLE IF EXISTS public."CourseContents";
DROP SEQUENCE IF EXISTS public."Certificates_Id_seq";
DROP TABLE IF EXISTS public."Certificates";
DROP TABLE IF EXISTS public."CartItems";
DROP SEQUENCE IF EXISTS public."BackupCodes_Id_seq";
DROP TABLE IF EXISTS public."BackupCodes";
DROP SEQUENCE IF EXISTS public."Announcements_Id_seq";
DROP TABLE IF EXISTS public."Announcements";
DROP TABLE IF EXISTS public."AnalyticsEvents";
DROP FUNCTION IF EXISTS public.update_updated_at_column();
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
    "CreatedAt" timestamp with time zone DEFAULT CURRENT_TIMESTAMP
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
    "UpdatedAt" timestamp with time zone
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
    "VerifiedAt" timestamp with time zone
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
    "CertificateUrl" text
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
    "IsDeleted" boolean DEFAULT false
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
-- Name: Favorites; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Favorites" (
    "Id" integer NOT NULL,
    "UserId" integer NOT NULL,
    "SubjectId" integer NOT NULL,
    "AddedAt" timestamp with time zone NOT NULL
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
    "PriceAtPurchase" numeric(10,2) NOT NULL
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
    "UserId" integer NOT NULL,
    "OrderNumber" character varying(50) NOT NULL,
    "TotalAmount" numeric(12,2) NOT NULL,
    "Status" character varying(50) NOT NULL,
    "PaymentMethod" character varying(50),
    "TransactionId" text,
    "OrderDate" timestamp with time zone NOT NULL,
    "CompletedDate" timestamp with time zone,
    "Notes" text
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
    "UpdatedBy" integer
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
    "IsDeleted" boolean DEFAULT false NOT NULL
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
    "IsDeleted" boolean DEFAULT false
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
    "IsDeleted" boolean DEFAULT false
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
    "Tags" jsonb DEFAULT '[]'::jsonb
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
    "UpdatedAt" timestamp with time zone
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
    "ExpiresAt" timestamp without time zone NOT NULL,
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
    "EmailVerified" boolean DEFAULT false NOT NULL
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
    currency character varying(3) DEFAULT 'EUR'::character varying,
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
-- Name: payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payments (
    "Id" integer NOT NULL,
    "OrderId" integer NOT NULL,
    "UserId" integer NOT NULL,
    "Amount" numeric(10,2) NOT NULL,
    "Currency" character varying(3) NOT NULL,
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
    "UpdatedAt" timestamp with time zone NOT NULL
);


--
-- Name: payments_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.payments ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
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
-- Name: BackupCodes Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BackupCodes" ALTER COLUMN "Id" SET DEFAULT nextval('public."BackupCodes_Id_seq"'::regclass);


--
-- Name: Certificates Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Certificates" ALTER COLUMN "Id" SET DEFAULT nextval('public."Certificates_Id_seq"'::regclass);


--
-- Name: DeviceInfos Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DeviceInfos" ALTER COLUMN "Id" SET DEFAULT nextval('public."DeviceInfos_Id_seq"'::regclass);


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
-- Name: Goals Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Goals" ALTER COLUMN "Id" SET DEFAULT nextval('public."Goals_Id_seq"'::regclass);


--
-- Name: HomePageFeatures Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."HomePageFeatures" ALTER COLUMN "Id" SET DEFAULT nextval('public."HomePageFeatures_Id_seq"'::regclass);


--
-- Name: Institutions Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Institutions" ALTER COLUMN "Id" SET DEFAULT nextval('public."Institutions_Id_seq"'::regclass);


--
-- Name: Levels Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Levels" ALTER COLUMN "Id" SET DEFAULT nextval('public."Levels_Id_seq"'::regclass);


--
-- Name: OAuthAccounts Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OAuthAccounts" ALTER COLUMN "Id" SET DEFAULT nextval('public."OAuthAccounts_Id_seq"'::regclass);


--
-- Name: Pages Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Pages" ALTER COLUMN "Id" SET DEFAULT nextval('public."Pages_Id_seq"'::regclass);


--
-- Name: PasswordResetTokens Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PasswordResetTokens" ALTER COLUMN "Id" SET DEFAULT nextval('public."PasswordResetTokens_Id_seq"'::regclass);


--
-- Name: PricingPlans Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PricingPlans" ALTER COLUMN "Id" SET DEFAULT nextval('public."PricingPlans_Id_seq"'::regclass);


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
-- Name: Revisions Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Revisions" ALTER COLUMN "Id" SET DEFAULT nextval('public."Revisions_Id_seq"'::regclass);


--
-- Name: Sessions Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Sessions" ALTER COLUMN "Id" SET DEFAULT nextval('public."Sessions_Id_seq"'::regclass);


--
-- Name: Subscriptions Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Subscriptions" ALTER COLUMN "Id" SET DEFAULT nextval('public."Subscriptions_Id_seq"'::regclass);


--
-- Name: TwoFactorTokens Id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TwoFactorTokens" ALTER COLUMN "Id" SET DEFAULT nextval('public."TwoFactorTokens_Id_seq"'::regclass);


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
-- Data for Name: BackupCodes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."BackupCodes" ("Id", "TwoFactorTokenId", "Code", "IsUsed", "UsedAt", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: CartItems; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."CartItems" ("Id", "UserId", "SubjectId", "Price", "AddedAt") FROM stdin;
1	1	26	49.99	2026-02-16 13:09:19.142134+00
2	1	20	29.99	2026-02-17 13:09:19.142134+00
3	2	19	44.99	2026-02-15 13:09:19.142134+00
4	5	8	27.99	2026-02-17 13:09:19.142134+00
5	9	1	29.99	2026-02-14 13:09:19.142134+00
\.


--
-- Data for Name: Certificates; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Certificates" ("Id", "UserId", "EnrollmentId", "SubjectId", "Title", "CertificateUrl", "CertificateNumber", "FinalScore", "IssuedAt", "CreatedAt") FROM stdin;
1	2	4	1	Certificat - Mathématiques Terminale S	https://certs.winplus.cm/cert/WPC-2026-0001.pdf	WPC-2026-0001	92.50	2026-02-15 16:00:00+00	2026-02-18 14:55:59.537933+00
2	2	5	3	Certificat - Français Dissertation	https://certs.winplus.cm/cert/WPC-2026-0002.pdf	WPC-2026-0002	88.00	2026-02-10 14:00:00+00	2026-02-18 14:55:59.537933+00
3	4	9	5	Certificat - Anglais Conversationnel	https://certs.winplus.cm/cert/WPC-2026-0003.pdf	WPC-2026-0003	85.50	2026-02-12 11:00:00+00	2026-02-18 14:55:59.537933+00
\.


--
-- Data for Name: CourseContents; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."CourseContents" ("Id", "SubjectId", "Title", "Description", "VideoUrl", "DocumentUrl", "OrderIndex", "DurationMinutes", "IsLocked", "CreatedAt", "UpdatedAt") FROM stdin;
3	1	Introduction aux Limites	Comprendre le concept de limite d'une fonction.	https://videos.winplus.cm/maths/limites-intro.mp4	\N	1	45	f	2026-01-05 10:00:00+00	\N
4	1	Calcul de Limites	Méthodes et techniques de calcul de limites.	https://videos.winplus.cm/maths/limites-calcul.mp4	\N	2	50	f	2026-01-05 10:00:00+00	\N
5	1	Dérivées - Fondamentaux	Définition et règles de dérivation.	https://videos.winplus.cm/maths/derivees-bases.mp4	\N	3	55	f	2026-01-06 10:00:00+00	\N
6	1	Applications des Dérivées	Étude de fonctions, tangentes et optimisation.	https://videos.winplus.cm/maths/derivees-applications.mp4	\N	4	60	t	2026-01-07 10:00:00+00	\N
7	1	Intégrales - Introduction	Concept d'intégrale et primitives.	https://videos.winplus.cm/maths/integrales-intro.mp4	\N	5	50	t	2026-01-08 10:00:00+00	\N
8	2	Les Lois de Newton	Les 3 lois fondamentales de la mécanique.	https://videos.winplus.cm/physique/newton.mp4	\N	1	40	f	2026-01-05 10:00:00+00	\N
9	2	Énergie Cinétique et Potentielle	Conservation et transformation de l'énergie.	https://videos.winplus.cm/physique/energie.mp4	\N	2	45	f	2026-01-06 10:00:00+00	\N
10	2	Réactions Chimiques	Équilibrer les équations et stœchiométrie.	https://videos.winplus.cm/physique/reactions.mp4	\N	3	50	t	2026-01-07 10:00:00+00	\N
11	3	Méthodologie de la Dissertation	Les étapes clés pour rédiger une dissertation réussie.	https://videos.winplus.cm/francais/methodo-dissert.mp4	\N	1	40	f	2026-01-05 10:00:00+00	\N
12	3	Construire un Plan	Thèse, antithèse, synthèse et plan dialectique.	https://videos.winplus.cm/francais/plan.mp4	\N	2	35	f	2026-01-06 10:00:00+00	\N
13	3	L'Introduction Parfaite	Accrocher le lecteur et poser la problématique.	https://videos.winplus.cm/francais/intro.mp4	\N	3	30	f	2026-01-07 10:00:00+00	\N
14	3	Exemples et Arguments	Illustrer avec des références littéraires pertinentes.	https://videos.winplus.cm/francais/exemples.mp4	\N	4	45	t	2026-01-08 10:00:00+00	\N
15	5	Greetings & Introductions	Learn how to introduce yourself naturally.	https://videos.winplus.cm/anglais/greetings.mp4	\N	1	30	f	2026-01-05 10:00:00+00	\N
16	5	Everyday Conversations	Practice common daily dialogues and expressions.	https://videos.winplus.cm/anglais/everyday.mp4	\N	2	35	f	2026-01-06 10:00:00+00	\N
17	5	Pronunciation Workshop	Master tricky English sounds and intonation.	https://videos.winplus.cm/anglais/pronunciation.mp4	\N	3	40	f	2026-01-07 10:00:00+00	\N
18	9	Introduction à Python	Installation, variables et premiers programmes.	https://videos.winplus.cm/python/intro.mp4	\N	1	45	f	2026-01-05 10:00:00+00	\N
19	9	Structures de Contrôle	Conditions if/else, boucles for et while.	https://videos.winplus.cm/python/controle.mp4	\N	2	50	f	2026-01-06 10:00:00+00	\N
20	9	Fonctions et Modules	Créer des fonctions réutilisables et importer des modules.	https://videos.winplus.cm/python/fonctions.mp4	\N	3	45	f	2026-01-07 10:00:00+00	\N
21	9	Listes et Dictionnaires	Manipuler les structures de données essentielles.	https://videos.winplus.cm/python/listes.mp4	\N	4	50	f	2026-01-08 10:00:00+00	\N
22	9	Projet Final - Jeu en Python	Créer un jeu complet en utilisant toutes les notions apprises.	https://videos.winplus.cm/python/projet-jeu.mp4	\N	5	60	t	2026-01-09 10:00:00+00	\N
23	26	React Fondamentaux	JSX, composants, props et state.	https://videos.winplus.cm/react/fondamentaux.mp4	\N	1	55	f	2026-01-15 10:00:00+00	\N
24	26	Hooks Essentiels	useState, useEffect et cycle de vie.	https://videos.winplus.cm/react/hooks.mp4	\N	2	50	f	2026-01-16 10:00:00+00	\N
25	26	Routing avec Next.js	Pages, navigation et layouts avec Next.js.	https://videos.winplus.cm/react/routing.mp4	\N	3	45	t	2026-01-17 10:00:00+00	\N
26	26	API Routes & Data Fetching	getServerSideProps, getStaticProps et API routes.	https://videos.winplus.cm/react/api-routes.mp4	\N	4	55	t	2026-01-18 10:00:00+00	\N
27	20	Introduction au Marketing Digital	Les fondamentaux du marketing en ligne.	https://videos.winplus.cm/marketing/intro.mp4	\N	1	40	f	2026-01-10 10:00:00+00	\N
28	20	Réseaux Sociaux & Stratégie	Créer une présence efficace sur les réseaux sociaux.	https://videos.winplus.cm/marketing/reseaux.mp4	\N	2	45	f	2026-01-11 10:00:00+00	\N
29	20	SEO & Référencement	Optimiser son site pour les moteurs de recherche.	https://videos.winplus.cm/marketing/seo.mp4	\N	3	50	t	2026-01-12 10:00:00+00	\N
30	8	La Cellule Humaine	Structure et fonctions de la cellule.	https://videos.winplus.cm/bio/cellule.mp4	\N	1	45	f	2026-01-05 10:00:00+00	\N
31	8	Le Système Digestif	Anatomie et physiologie de la digestion.	https://videos.winplus.cm/bio/digestif.mp4	\N	2	40	f	2026-01-06 10:00:00+00	\N
32	8	Le Système Nerveux	Neurones, synapses et transmission nerveuse.	https://videos.winplus.cm/bio/nerveux.mp4	\N	3	50	f	2026-01-07 10:00:00+00	\N
\.


--
-- Data for Name: DeviceInfos; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."DeviceInfos" ("Id", "UserId", "DeviceFingerprint", "UserAgent", "IpAddress", "BrowserName", "BrowserVersion", "OSName", "OSVersion", "DeviceName", "RememberUntil", "LastUsedAt", "CreatedAt") FROM stdin;
1	1	axLLNlTRlCVkKIoY1qASd+woUX+LLd5guE8Qs3INYBw=	Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36	146.70.246.105	Chrome	145	Windows	10.0	Windows PC	\N	2026-02-19 16:46:44.889973+00	2026-02-19 16:39:34.541576+00
\.


--
-- Data for Name: EmailVerificationTokens; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."EmailVerificationTokens" ("Id", "UserId", "VerificationCode", "ExpiresAt", "IsVerified", "AttemptCount", "CreatedAt", "VerifiedAt") FROM stdin;
1	1	320555	2026-02-19 12:58:48.42235+00	f	0	2026-02-18 12:58:48.422313+00	\N
\.


--
-- Data for Name: Enrollments; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Enrollments" ("Id", "UserId", "SubjectId", "EnrolledAt", "CompletedAt", "ProgressPercentage", "IsCompleted", "CertificateUrl") FROM stdin;
7	3	2	2025-12-05 13:09:19.135568+00	\N	100.00	t	\N
8	3	8	2025-12-25 13:09:19.135568+00	\N	50.00	f	\N
13	7	1	2025-12-22 13:09:19.135568+00	\N	100.00	t	\N
14	7	3	2026-01-01 13:09:19.135568+00	\N	65.00	f	\N
15	9	5	2026-01-01 13:09:19.135568+00	\N	80.00	f	\N
16	9	9	2026-01-21 13:09:19.135568+00	\N	15.00	f	\N
17	12	26	2026-01-19 13:09:19.135568+00	\N	55.00	f	\N
1	1	1	2025-11-30 13:09:19.135568+00	\N	60.00	t	\N
2	1	9	2025-12-20 13:09:19.135568+00	\N	80.00	f	\N
3	1	19	2026-01-09 13:09:19.135568+00	\N	40.00	f	\N
6	2	5	2025-12-30 13:09:19.135568+00	\N	75.00	f	\N
10	4	20	2026-01-04 13:09:19.135568+00	\N	30.00	f	\N
11	5	9	2025-12-15 13:09:19.135568+00	\N	50.00	f	\N
12	5	26	2026-01-14 13:09:19.135568+00	\N	45.00	f	\N
18	13	9	2026-01-24 13:09:19.135568+00	\N	70.00	f	\N
19	13	19	2026-02-03 13:09:19.135568+00	\N	50.00	f	\N
4	2	1	2025-12-02 13:09:19.135568+00	2026-02-15 16:00:00+00	100.00	t	https://certs.winplus.cm/cert/WPC-2026-0001.pdf
5	2	3	2025-12-10 13:09:19.135568+00	2026-02-10 14:00:00+00	100.00	t	https://certs.winplus.cm/cert/WPC-2026-0002.pdf
9	4	5	2025-12-08 13:09:19.135568+00	2026-02-12 11:00:00+00	100.00	t	https://certs.winplus.cm/cert/WPC-2026-0003.pdf
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

COPY public."Exams" ("Id", "Title", "ExamType", "SubjectId", "Category", "Year", "Session", "Level", "Duration", "DocumentUrl", "CorrectionUrl", "Description", "Difficulty", "DownloadCount", "IsPublished", "CreatedAt", "UpdatedAt", "IsDeleted") FROM stdin;
1	BAC C - Mathématiques 2025	Baccalauréat	1	Mathématiques	2025	session_1	Terminale C	240	https://docs.winplus.cm/exams/bac/bac-c-maths-2025.pdf	https://docs.winplus.cm/exams/bac/bac-c-maths-2025-corrige.pdf	Épreuve de mathématiques du Baccalauréat série C session 2025.	difficile	1245	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
2	BAC C - Physique-Chimie 2025	Baccalauréat	2	Sciences	2025	session_1	Terminale C	180	https://docs.winplus.cm/exams/bac/bac-c-pc-2025.pdf	https://docs.winplus.cm/exams/bac/bac-c-pc-2025-corrige.pdf	Épreuve de physique-chimie du Baccalauréat série C session 2025.	difficile	1089	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
3	BAC A - Français 2025	Baccalauréat	3	Lettres	2025	session_1	Terminale A	240	https://docs.winplus.cm/exams/bac/bac-a-francais-2025.pdf	https://docs.winplus.cm/exams/bac/bac-a-francais-2025-corrige.pdf	Dissertation et commentaire composé - BAC série A 2025.	moyen	987	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
4	BAC A - Philosophie 2025	Baccalauréat	7	Philosophie	2025	session_1	Terminale A	240	https://docs.winplus.cm/exams/bac/bac-a-philo-2025.pdf	\N	Épreuve de philosophie BAC A 2025.	moyen	876	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
5	BAC C - SVT 2025	Baccalauréat	8	Sciences	2025	session_1	Terminale C	180	https://docs.winplus.cm/exams/bac/bac-c-svt-2025.pdf	https://docs.winplus.cm/exams/bac/bac-c-svt-2025-corrige.pdf	Sciences de la Vie et de la Terre - BAC C 2025.	moyen	756	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
6	BAC - Anglais 2025	Baccalauréat	5	Langues	2025	session_1	Toutes séries	180	https://docs.winplus.cm/exams/bac/bac-anglais-2025.pdf	https://docs.winplus.cm/exams/bac/bac-anglais-2025-corrige.pdf	Épreuve d'anglais toutes séries BAC 2025.	moyen	1345	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
7	BAC D - Mathématiques 2025	Baccalauréat	1	Mathématiques	2025	session_1	Terminale D	240	https://docs.winplus.cm/exams/bac/bac-d-maths-2025.pdf	https://docs.winplus.cm/exams/bac/bac-d-maths-2025-corrige.pdf	Épreuve de maths BAC série D 2025.	moyen	934	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
8	BAC - Histoire-Géographie 2025	Baccalauréat	4	Histoire	2025	session_1	Toutes séries	180	https://docs.winplus.cm/exams/bac/bac-hg-2025.pdf	\N	Histoire-Géographie BAC 2025.	moyen	678	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
9	BAC - Informatique 2025	Baccalauréat	9	Informatique	2025	session_1	Terminale C/D/TI	120	https://docs.winplus.cm/exams/bac/bac-info-2025.pdf	https://docs.winplus.cm/exams/bac/bac-info-2025-corrige.pdf	Épreuve d'informatique BAC 2025.	facile	567	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
10	BAC C - Mathématiques 2024	Baccalauréat	1	Mathématiques	2024	session_1	Terminale C	240	https://docs.winplus.cm/exams/bac/bac-c-maths-2024.pdf	https://docs.winplus.cm/exams/bac/bac-c-maths-2024-corrige.pdf	Épreuve de mathématiques BAC C 2024.	difficile	2345	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
11	BAC C - Physique-Chimie 2024	Baccalauréat	2	Sciences	2024	session_1	Terminale C	180	https://docs.winplus.cm/exams/bac/bac-c-pc-2024.pdf	https://docs.winplus.cm/exams/bac/bac-c-pc-2024-corrige.pdf	Physique-Chimie BAC C 2024.	difficile	1876	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
12	BAC A - Français 2024	Baccalauréat	3	Lettres	2024	session_1	Terminale A	240	https://docs.winplus.cm/exams/bac/bac-a-francais-2024.pdf	https://docs.winplus.cm/exams/bac/bac-a-francais-2024-corrige.pdf	Français BAC A 2024.	moyen	1567	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
13	BAC D - Mathématiques 2024	Baccalauréat	1	Mathématiques	2024	session_1	Terminale D	240	https://docs.winplus.cm/exams/bac/bac-d-maths-2024.pdf	https://docs.winplus.cm/exams/bac/bac-d-maths-2024-corrige.pdf	Maths BAC D 2024.	moyen	1678	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
14	BAC - Anglais 2024	Baccalauréat	5	Langues	2024	session_1	Toutes séries	180	https://docs.winplus.cm/exams/bac/bac-anglais-2024.pdf	https://docs.winplus.cm/exams/bac/bac-anglais-2024-corrige.pdf	Anglais BAC 2024.	moyen	1890	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
15	BAC C - Mathématiques 2023	Baccalauréat	1	Mathématiques	2023	session_1	Terminale C	240	https://docs.winplus.cm/exams/bac/bac-c-maths-2023.pdf	https://docs.winplus.cm/exams/bac/bac-c-maths-2023-corrige.pdf	Maths BAC C 2023.	difficile	3456	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
16	BAC A - Français 2023	Baccalauréat	3	Lettres	2023	session_1	Terminale A	240	https://docs.winplus.cm/exams/bac/bac-a-francais-2023.pdf	https://docs.winplus.cm/exams/bac/bac-a-francais-2023-corrige.pdf	Français BAC A 2023.	moyen	2345	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
17	Probatoire C - Mathématiques 2025	Probatoire	1	Mathématiques	2025	session_1	Première C	180	https://docs.winplus.cm/exams/probatoire/prob-c-maths-2025.pdf	https://docs.winplus.cm/exams/probatoire/prob-c-maths-2025-corrige.pdf	Épreuve de maths Probatoire série C 2025.	moyen	789	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
18	Probatoire C - Physique-Chimie 2025	Probatoire	2	Sciences	2025	session_1	Première C	150	https://docs.winplus.cm/exams/probatoire/prob-c-pc-2025.pdf	https://docs.winplus.cm/exams/probatoire/prob-c-pc-2025-corrige.pdf	Physique-Chimie Probatoire C 2025.	moyen	654	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
19	Probatoire A - Français 2025	Probatoire	3	Lettres	2025	session_1	Première A	180	https://docs.winplus.cm/exams/probatoire/prob-a-francais-2025.pdf	\N	Français Probatoire A 2025.	moyen	543	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
20	Probatoire D - SVT 2025	Probatoire	8	Sciences	2025	session_1	Première D	150	https://docs.winplus.cm/exams/probatoire/prob-d-svt-2025.pdf	https://docs.winplus.cm/exams/probatoire/prob-d-svt-2025-corrige.pdf	SVT Probatoire D 2025.	moyen	432	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
21	Probatoire - Anglais 2025	Probatoire	5	Langues	2025	session_1	Toutes séries	150	https://docs.winplus.cm/exams/probatoire/prob-anglais-2025.pdf	https://docs.winplus.cm/exams/probatoire/prob-anglais-2025-corrige.pdf	Anglais Probatoire 2025.	facile	567	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
22	Probatoire C - Mathématiques 2024	Probatoire	1	Mathématiques	2024	session_1	Première C	180	https://docs.winplus.cm/exams/probatoire/prob-c-maths-2024.pdf	https://docs.winplus.cm/exams/probatoire/prob-c-maths-2024-corrige.pdf	Maths Probatoire C 2024.	moyen	1234	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
23	Probatoire A - Français 2024	Probatoire	3	Lettres	2024	session_1	Première A	180	https://docs.winplus.cm/exams/probatoire/prob-a-francais-2024.pdf	https://docs.winplus.cm/exams/probatoire/prob-a-francais-2024-corrige.pdf	Français Probatoire A 2024.	moyen	987	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
24	BEPC - Mathématiques 2025	BEPC	1	Mathématiques	2025	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-maths-2025.pdf	https://docs.winplus.cm/exams/bepc/bepc-maths-2025-corrige.pdf	Épreuve de mathématiques BEPC 2025.	facile	2345	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
25	BEPC - Français 2025	BEPC	3	Lettres	2025	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-francais-2025.pdf	https://docs.winplus.cm/exams/bepc/bepc-francais-2025-corrige.pdf	Français BEPC 2025 - Dictée, rédaction, grammaire.	facile	2123	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
26	BEPC - Sciences 2025	BEPC	2	Sciences	2025	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-sciences-2025.pdf	https://docs.winplus.cm/exams/bepc/bepc-sciences-2025-corrige.pdf	Sciences (Physique + SVT) BEPC 2025.	facile	1876	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
27	BEPC - Anglais 2025	BEPC	5	Langues	2025	session_1	Troisième	90	https://docs.winplus.cm/exams/bepc/bepc-anglais-2025.pdf	https://docs.winplus.cm/exams/bepc/bepc-anglais-2025-corrige.pdf	Anglais BEPC 2025.	facile	1654	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
28	BEPC - Histoire-Géographie 2025	BEPC	4	Histoire	2025	session_1	Troisième	90	https://docs.winplus.cm/exams/bepc/bepc-hg-2025.pdf	\N	Histoire-Géographie BEPC 2025.	facile	1432	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
29	BEPC - Mathématiques 2024	BEPC	1	Mathématiques	2024	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-maths-2024.pdf	https://docs.winplus.cm/exams/bepc/bepc-maths-2024-corrige.pdf	Maths BEPC 2024.	facile	3456	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
30	BEPC - Français 2024	BEPC	3	Lettres	2024	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-francais-2024.pdf	https://docs.winplus.cm/exams/bepc/bepc-francais-2024-corrige.pdf	Français BEPC 2024.	facile	3123	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
31	BEPC - Sciences 2024	BEPC	2	Sciences	2024	session_1	Troisième	120	https://docs.winplus.cm/exams/bepc/bepc-sciences-2024.pdf	https://docs.winplus.cm/exams/bepc/bepc-sciences-2024-corrige.pdf	Sciences BEPC 2024.	facile	2876	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
32	ENS Yaoundé - Mathématiques 2025	ENS	1	Mathématiques	2025	session_1	1ère année	240	https://docs.winplus.cm/exams/ens/maths-2025.pdf	https://docs.winplus.cm/exams/ens/maths-2025-corrige.pdf	Concours ENS Yaoundé - Mathématiques 2025.	difficile	342	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
33	ENS Yaoundé - Mathématiques 2024	ENS	1	Mathématiques	2024	session_1	1ère année	240	https://docs.winplus.cm/exams/ens/maths-2024.pdf	https://docs.winplus.cm/exams/ens/maths-2024-corrige.pdf	Concours ENS Yaoundé - Mathématiques 2024.	difficile	567	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
34	ENS Yaoundé - Physique 2025	ENS	2	Sciences	2025	session_1	1ère année	180	https://docs.winplus.cm/exams/ens/physique-2025.pdf	https://docs.winplus.cm/exams/ens/physique-2025-corrige.pdf	Concours ENS - Physique 2025.	difficile	298	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
35	ENS Yaoundé - Informatique 2025	ENS	9	Informatique	2025	session_1	1ère année	180	https://docs.winplus.cm/exams/ens/info-2025.pdf	https://docs.winplus.cm/exams/ens/info-2025-corrige.pdf	Algorithmique et programmation ENS 2025.	difficile	412	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
36	ENS Yaoundé - Français 2025	ENS	3	Lettres	2025	session_1	1ère année	240	https://docs.winplus.cm/exams/ens/francais-2025.pdf	\N	Dissertation et commentaire composé ENS 2025.	moyen	234	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
37	Polytechnique - Mathématiques 2025	Polytechnique	1	Mathématiques	2025	session_1	Cycle ingénieur	300	https://docs.winplus.cm/exams/polytech/maths-2025.pdf	https://docs.winplus.cm/exams/polytech/maths-2025-corrige.pdf	Concours Polytechnique Yaoundé - Maths 2025.	difficile	623	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
38	Polytechnique - Mathématiques 2024	Polytechnique	1	Mathématiques	2024	session_1	Cycle ingénieur	300	https://docs.winplus.cm/exams/polytech/maths-2024.pdf	https://docs.winplus.cm/exams/polytech/maths-2024-corrige.pdf	Concours Polytechnique - Maths 2024.	difficile	812	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
39	Polytechnique - Physique 2025	Polytechnique	2	Sciences	2025	session_1	Cycle ingénieur	240	https://docs.winplus.cm/exams/polytech/physique-2025.pdf	https://docs.winplus.cm/exams/polytech/physique-2025-corrige.pdf	Physique Polytechnique 2025.	difficile	478	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
40	Polytechnique - Informatique 2025	Polytechnique	9	Informatique	2025	session_1	Cycle ingénieur	240	https://docs.winplus.cm/exams/polytech/info-2025.pdf	https://docs.winplus.cm/exams/polytech/info-2025-corrige.pdf	Informatique Polytechnique 2025.	difficile	389	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
41	ENAM - Culture Générale 2025	ENAM	\N	Lettres	2025	session_1	Division administrative	240	https://docs.winplus.cm/exams/enam/culture-gen-2025.pdf	\N	Culture générale ENAM 2025.	moyen	345	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
42	ENAM - Droit Civil 2025	ENAM	27	Droit	2025	session_1	Division judiciaire	240	https://docs.winplus.cm/exams/enam/droit-civil-2025.pdf	\N	Droit civil ENAM 2025.	difficile	278	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
43	FMSB - Biologie 2025	FMSB	8	Sciences	2025	session_1	1ère année médecine	180	https://docs.winplus.cm/exams/fmsb/bio-2025.pdf	https://docs.winplus.cm/exams/fmsb/bio-2025-corrige.pdf	Biologie concours médecine 2025.	difficile	567	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
44	FMSB - Chimie 2025	FMSB	12	Sciences	2025	session_1	1ère année médecine	180	https://docs.winplus.cm/exams/fmsb/chimie-2025.pdf	\N	Chimie organique FMSB 2025.	difficile	445	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
45	FMSB - Physique 2025	FMSB	2	Sciences	2025	session_1	1ère année médecine	180	https://docs.winplus.cm/exams/fmsb/physique-2025.pdf	https://docs.winplus.cm/exams/fmsb/physique-2025-corrige.pdf	Biophysique FMSB 2025.	difficile	389	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
46	ESSEC - Maths Financières 2025	ESSEC	16	Mathématiques	2025	session_1	Licence 1	180	https://docs.winplus.cm/exams/essec/maths-fin-2025.pdf	https://docs.winplus.cm/exams/essec/maths-fin-2025-corrige.pdf	Maths financières ESSEC 2025.	moyen	289	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
47	ENSET - Informatique 2025	ENSET	9	Informatique	2025	session_1	1ère année	180	https://docs.winplus.cm/exams/enset/info-2025.pdf	https://docs.winplus.cm/exams/enset/info-2025-corrige.pdf	Informatique ENSET 2025.	moyen	312	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
48	IUT Douala - Informatique 2025	IUT	9	Informatique	2025	session_1	DUT	120	https://docs.winplus.cm/exams/iut/info-2025.pdf	https://docs.winplus.cm/exams/iut/info-2025-corrige.pdf	Programmation et BD IUT 2025.	facile	198	t	2026-02-18 14:55:12.835904+00	2026-02-18 14:55:12.835904+00	f
\.


--
-- Data for Name: Favorites; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Favorites" ("Id", "UserId", "SubjectId", "AddedAt") FROM stdin;
1	1	5	2025-12-10 13:09:19.139554+00
2	1	26	2025-12-30 13:09:19.139554+00
3	1	20	2026-01-19 13:09:19.139554+00
4	2	9	2025-12-15 13:09:19.139554+00
5	2	19	2026-01-04 13:09:19.139554+00
6	3	1	2025-12-20 13:09:19.139554+00
7	4	26	2025-12-25 13:09:19.139554+00
8	5	19	2025-12-30 13:09:19.139554+00
9	7	9	2026-01-09 13:09:19.139554+00
10	9	5	2026-01-14 13:09:19.139554+00
\.


--
-- Data for Name: Goals; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Goals" ("Id", "UserId", "Title", "Description", "Type", "TargetDate", "Status", "Progress", "CreatedAt", "UpdatedAt") FROM stdin;
1	1	Obtenir le BAC C avec mention	Préparer toutes les matières du BAC série C pour la session 2026.	exam	2026-06-15 00:00:00+00	in_progress	45.00	2026-02-20 15:01:27.52467+00	2026-02-20 15:01:27.52467+00
2	1	Maîtriser Python	Compléter le cours Python et réaliser le projet final.	skill	2026-04-01 00:00:00+00	in_progress	80.00	2026-02-20 15:01:27.52467+00	2026-02-20 15:01:27.52467+00
3	1	Apprendre React & Next.js	Devenir capable de créer des applications web complètes.	skill	2026-05-01 00:00:00+00	in_progress	40.00	2026-02-20 15:01:27.52467+00	2026-02-20 15:01:27.52467+00
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
19	1	1	3	lesson_view	2700	\N	2026-02-10 14:00:00+00	2026-02-10 14:00:00+00	\N	\N	\N	Introduction aux Limites	lesson	t	\N	100	0	2026-02-10 14:45:00+00
20	1	1	4	lesson_view	3000	\N	2026-02-11 15:00:00+00	2026-02-11 15:00:00+00	\N	\N	\N	Calcul de Limites	lesson	t	\N	100	0	2026-02-11 15:50:00+00
21	1	1	5	lesson_view	1800	\N	2026-02-12 14:30:00+00	2026-02-12 14:30:00+00	\N	\N	\N	Dérivées - Fondamentaux	lesson	f	\N	60	0	2026-02-12 15:00:00+00
22	1	9	18	lesson_view	2700	\N	2026-02-13 10:00:00+00	2026-02-13 10:00:00+00	\N	\N	\N	Introduction à Python	lesson	t	\N	100	0	2026-02-13 10:45:00+00
23	1	9	19	lesson_view	3000	\N	2026-02-14 11:00:00+00	2026-02-14 11:00:00+00	\N	\N	\N	Structures de Contrôle	lesson	t	\N	100	0	2026-02-14 11:50:00+00
24	1	9	20	quiz_attempt	600	85.00	2026-02-15 09:00:00+00	2026-02-15 09:00:00+00	\N	\N	\N	Quiz - Fonctions Python	quiz	t	\N	100	85	2026-02-15 09:10:00+00
25	1	19	\N	lesson_view	2400	\N	2026-02-16 16:00:00+00	2026-02-16 16:00:00+00	\N	\N	\N	Async/Await en JS	lesson	f	\N	40	0	2026-02-16 16:40:00+00
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
-- Data for Name: Notifications; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Notifications" ("Id", "UserId", "Title", "Message", "Type", "RelatedEntityType", "RelatedEntityId", "IsRead", "CreatedAt", "ReadAt") FROM stdin;
1	1	Inscription confirmée	Vous êtes inscrit au cours Mathématiques Terminale S.	enrollment	Subject	1	t	2026-01-10 10:32:00+00	\N
2	1	Inscription confirmée	Vous êtes inscrit au cours Informatique Python.	enrollment	Subject	9	t	2026-01-12 14:02:00+00	\N
3	1	Badge obtenu !	Félicitations ! Vous avez obtenu le badge "Premier Pas".	badge	Badge	1	t	2026-01-10 10:33:00+00	\N
4	1	Nouveau cours disponible	Le cours React & Next.js vient d'être publié. Découvrez-le !	info	Subject	26	f	2026-02-15 08:00:00+00	\N
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

COPY public."OrderItems" ("Id", "OrderId", "SubjectId", "PriceAtPurchase") FROM stdin;
\.


--
-- Data for Name: Orders; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Orders" ("Id", "UserId", "OrderNumber", "TotalAmount", "Status", "PaymentMethod", "TransactionId", "OrderDate", "CompletedDate", "Notes") FROM stdin;
1	1	WP-2026-0001	29.99	completed	mobile_money	TXN-MTN-001	2026-01-10 10:30:00+00	2026-01-10 10:31:00+00	Mathématiques Terminale S
2	1	WP-2026-0002	39.99	completed	mobile_money	TXN-MTN-002	2026-01-12 14:00:00+00	2026-01-12 14:01:00+00	Informatique Python
3	1	WP-2026-0003	44.99	completed	orange_money	TXN-OM-001	2026-01-20 09:15:00+00	2026-01-20 09:16:00+00	JavaScript Avancé
4	2	WP-2026-0004	29.99	completed	mobile_money	TXN-MTN-003	2025-12-15 11:00:00+00	2025-12-15 11:01:00+00	Mathématiques Terminale S
5	2	WP-2026-0005	19.99	completed	card	TXN-CARD-001	2025-12-18 16:30:00+00	2025-12-18 16:31:00+00	Français Dissertation
6	2	WP-2026-0006	34.99	completed	mobile_money	TXN-MTN-004	2025-12-20 08:45:00+00	2025-12-20 08:46:00+00	Anglais Conversationnel
7	4	WP-2026-0007	34.99	completed	orange_money	TXN-OM-002	2026-01-05 13:20:00+00	2026-01-05 13:21:00+00	Anglais Conversationnel
8	4	WP-2026-0008	29.99	completed	mobile_money	TXN-MTN-005	2026-01-08 10:00:00+00	2026-01-08 10:01:00+00	Marketing Digital
9	5	WP-2026-0009	39.99	completed	card	TXN-CARD-002	2026-01-15 15:30:00+00	2026-01-15 15:31:00+00	Informatique Python
10	5	WP-2026-0010	49.99	completed	mobile_money	TXN-MTN-006	2026-01-18 09:00:00+00	2026-01-18 09:01:00+00	React & Next.js
11	9	WP-2026-0011	34.99	completed	orange_money	TXN-OM-003	2026-01-22 11:00:00+00	2026-01-22 11:01:00+00	Anglais Conversationnel
12	9	WP-2026-0012	39.99	completed	mobile_money	TXN-MTN-007	2026-01-25 14:15:00+00	2026-01-25 14:16:00+00	Informatique Python
13	12	WP-2026-0013	49.99	completed	card	TXN-CARD-003	2026-02-01 10:30:00+00	2026-02-01 10:31:00+00	React & Next.js
14	13	WP-2026-0014	39.99	completed	mobile_money	TXN-MTN-008	2026-02-05 08:45:00+00	2026-02-05 08:46:00+00	Informatique Python
15	13	WP-2026-0015	44.99	completed	orange_money	TXN-OM-004	2026-02-10 16:00:00+00	2026-02-10 16:01:00+00	JavaScript Avancé
16	6	WP-2026-0016	19.99	pending	mobile_money	\N	2026-02-17 12:00:00+00	\N	Espagnol Débutant
17	8	WP-2026-0017	34.99	pending	orange_money	\N	2026-02-18 09:30:00+00	\N	Biologie Humaine
\.


--
-- Data for Name: Pages; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Pages" ("Id", "Slug", "Title", "Content", "MetaDescription", "MetaKeywords", "IsPublished", "PublishedAt", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy") FROM stdin;
1	about	À propos de WinPlus	<h2>Notre Mission</h2>\n<p>WinPlus est une plateforme éducative camerounaise dédiée à la réussite scolaire et académique. Fondée en 2025, notre mission est de rendre l'éducation de qualité accessible à tous les étudiants du Cameroun et d'Afrique francophone.</p>\n\n<h2>Ce que nous offrons</h2>\n<p>Notre plateforme propose des cours interactifs, des épreuves d'examens officiels (BAC, Probatoire, BEPC, Concours), des quiz adaptatifs générés par intelligence artificielle, et des fiches de révision complètes couvrant toutes les matières du programme camerounais.</p>\n\n<h2>Notre Équipe</h2>\n<p>WinPlus est portée par une équipe passionnée d'enseignants expérimentés et de développeurs talentueux, tous engagés pour l'excellence éducative. Nos professeurs sont des experts dans leurs domaines respectifs, avec des années d'expérience dans l'enseignement au Cameroun.</p>\n\n<h2>Notre Vision</h2>\n<p>Devenir la référence en matière d'éducation numérique en Afrique centrale, en combinant technologie de pointe et pédagogie adaptée au contexte local.</p>\n\n<h2>Contactez-nous</h2>\n<p>Email : contact@winplus.cm<br>Téléphone : +237 6XX XXX XXX<br>Adresse : Yaoundé, Cameroun</p>	WinPlus - Plateforme éducative camerounaise pour la réussite scolaire. Cours, épreuves BAC, Probatoire, BEPC et concours.	éducation, cameroun, bac, probatoire, bepc, concours, cours en ligne, winplus	t	2026-01-15 08:00:00+00	2026-02-20 15:00:15.141262+00	2026-02-20 15:00:15.141262+00	1	\N
2	terms	Conditions d'utilisation	<h2>1. Acceptation des conditions</h2>\n<p>En accédant et en utilisant la plateforme WinPlus (winplus.cm), vous acceptez d'être lié par les présentes conditions d'utilisation. Si vous n'acceptez pas ces conditions, veuillez ne pas utiliser notre service.</p>\n\n<h2>2. Description du service</h2>\n<p>WinPlus est une plateforme éducative en ligne proposant des cours, des épreuves d'examens, des quiz et des fiches de révision. Le service est accessible via navigateur web et applications mobiles.</p>\n\n<h2>3. Inscription et compte</h2>\n<p>Pour accéder à certaines fonctionnalités, vous devez créer un compte. Vous êtes responsable de la confidentialité de vos identifiants de connexion. Vous devez fournir des informations exactes et à jour lors de l'inscription.</p>\n\n<h2>4. Contenu et propriété intellectuelle</h2>\n<p>Tout le contenu disponible sur WinPlus (cours, vidéos, documents, quiz) est protégé par le droit d'auteur. Vous bénéficiez d'une licence personnelle, non transférable et non exclusive pour accéder au contenu dans le cadre de votre apprentissage.</p>\n\n<h2>5. Abonnements et paiements</h2>\n<p>Certains contenus sont gratuits, d'autres nécessitent un abonnement payant. Les prix sont affichés en FCFA. Les paiements sont acceptés par Mobile Money (MTN, Orange) et carte bancaire. Les abonnements se renouvellent automatiquement sauf annulation.</p>\n\n<h2>6. Politique de remboursement</h2>\n<p>Les demandes de remboursement peuvent être effectuées dans les 7 jours suivant l'achat si le contenu n'a pas été consulté à plus de 20%. Au-delà, aucun remboursement ne sera accordé.</p>\n\n<h2>7. Comportement des utilisateurs</h2>\n<p>Les utilisateurs s'engagent à ne pas partager leur compte, ne pas redistribuer le contenu, ne pas perturber le fonctionnement de la plateforme, et à respecter les autres utilisateurs dans les espaces communautaires.</p>\n\n<h2>8. Limitation de responsabilité</h2>\n<p>WinPlus s'efforce de fournir un contenu de qualité mais ne garantit pas les résultats aux examens. La plateforme est fournie "en l'état" et nous ne pouvons être tenus responsables des interruptions de service.</p>\n\n<h2>9. Modification des conditions</h2>\n<p>WinPlus se réserve le droit de modifier ces conditions à tout moment. Les utilisateurs seront notifiés des changements importants par email.</p>\n\n<h2>10. Droit applicable</h2>\n<p>Les présentes conditions sont régies par le droit camerounais. Tout litige sera soumis aux tribunaux compétents de Yaoundé.</p>\n\n<p><em>Dernière mise à jour : 15 janvier 2026</em></p>	Conditions d'utilisation de la plateforme éducative WinPlus.	conditions, utilisation, termes, winplus, légal	t	2026-01-15 08:00:00+00	2026-02-20 15:00:15.141262+00	2026-02-20 15:00:15.141262+00	1	\N
3	privacy	Politique de confidentialité	<h2>1. Collecte des données</h2>\n<p>WinPlus collecte les données suivantes lors de votre utilisation : informations d'inscription (nom, email, rôle), données de progression (cours suivis, scores, temps passé), données de paiement (traitées par nos partenaires sécurisés), et données techniques (adresse IP, navigateur, appareil).</p>\n\n<h2>2. Utilisation des données</h2>\n<p>Vos données sont utilisées pour : fournir et améliorer nos services, personnaliser votre expérience d'apprentissage, générer des recommandations adaptées via notre IA, communiquer avec vous (notifications, newsletters si vous y avez consenti), et établir des statistiques anonymisées.</p>\n\n<h2>3. Protection des données</h2>\n<p>Nous mettons en œuvre des mesures de sécurité techniques et organisationnelles pour protéger vos données : chiffrement SSL/TLS, hashage des mots de passe, accès restreint aux données personnelles, sauvegardes régulières sécurisées.</p>\n\n<h2>4. Partage des données</h2>\n<p>Vos données ne sont jamais vendues à des tiers. Elles peuvent être partagées avec : nos prestataires de paiement (MTN MoMo, Orange Money), nos hébergeurs (dans le respect du RGPD), et les autorités si requis par la loi.</p>\n\n<h2>5. Données des mineurs</h2>\n<p>Les utilisateurs de moins de 18 ans doivent avoir l'autorisation de leurs parents ou tuteurs légaux. Les comptes "Parent" permettent de superviser l'activité des enfants sur la plateforme.</p>\n\n<h2>6. Vos droits</h2>\n<p>Conformément à la loi camerounaise et aux bonnes pratiques internationales, vous avez le droit de : accéder à vos données personnelles, rectifier vos informations, supprimer votre compte et données, exporter vos données, et retirer votre consentement aux communications marketing.</p>\n\n<h2>7. Cookies</h2>\n<p>WinPlus utilise des cookies essentiels au fonctionnement du site et des cookies analytiques (anonymisés) pour améliorer nos services. Vous pouvez gérer vos préférences de cookies dans les paramètres de votre navigateur.</p>\n\n<h2>8. Contact</h2>\n<p>Pour toute question relative à vos données personnelles : privacy@winplus.cm</p>\n\n<p><em>Dernière mise à jour : 15 janvier 2026</em></p>	Politique de confidentialité WinPlus. Comment nous protégeons vos données personnelles.	confidentialité, données personnelles, vie privée, RGPD, winplus	t	2026-01-15 08:00:00+00	2026-02-20 15:00:15.141262+00	2026-02-20 15:00:15.141262+00	1	\N
4	faq	Questions Fréquentes	<h2>Général</h2>\n\n<h3>Qu'est-ce que WinPlus ?</h3>\n<p>WinPlus est une plateforme éducative en ligne camerounaise qui propose des cours, des épreuves d'examens officiels, des quiz interactifs et des fiches de révision pour les étudiants de tous niveaux.</p>\n\n<h3>WinPlus est-il gratuit ?</h3>\n<p>WinPlus propose des contenus gratuits et des contenus premium. Vous pouvez accéder à certains cours et épreuves gratuitement. Pour un accès illimité, consultez nos plans d'abonnement.</p>\n\n<h2>Compte et inscription</h2>\n\n<h3>Comment créer un compte ?</h3>\n<p>Cliquez sur "S'inscrire", remplissez le formulaire avec votre email et choisissez votre rôle (étudiant, enseignant ou parent). Vous pouvez aussi vous inscrire via Google.</p>\n\n<h3>J'ai oublié mon mot de passe</h3>\n<p>Cliquez sur "Mot de passe oublié" sur la page de connexion. Un lien de réinitialisation vous sera envoyé par email.</p>\n\n<h2>Paiements</h2>\n\n<h3>Quels moyens de paiement acceptez-vous ?</h3>\n<p>Nous acceptons MTN Mobile Money, Orange Money et les cartes bancaires (Visa, Mastercard).</p>\n\n<h3>Puis-je me faire rembourser ?</h3>\n<p>Oui, dans les 7 jours suivant l'achat si vous n'avez pas consulté plus de 20% du contenu.</p>\n\n<h2>Épreuves et examens</h2>\n\n<h3>Quelles épreuves sont disponibles ?</h3>\n<p>Nous proposons des épreuves du BAC (séries A, C, D), du Probatoire, du BEPC, et des concours d'entrée aux grandes écoles (ENS, Polytechnique, ENAM, FMSB, ESSEC, etc.).</p>\n\n<h3>Les corrigés sont-ils inclus ?</h3>\n<p>La majorité des épreuves sont accompagnées de corrigés détaillés. Les épreuves sans corrigé sont clairement indiquées.</p>	FAQ WinPlus - Réponses à vos questions sur notre plateforme éducative.	faq, questions, aide, support, winplus	t	2026-01-20 08:00:00+00	2026-02-20 15:00:15.141262+00	2026-02-20 15:00:15.141262+00	1	\N
5	contact	Nous Contacter	<h2>Contactez l'équipe WinPlus</h2>\n\n<p>Nous sommes à votre écoute pour toute question, suggestion ou partenariat.</p>\n\n<h3>Email</h3>\n<p>Support : support@winplus.cm<br>\nPartenariats : partenaires@winplus.cm<br>\nPresse : presse@winplus.cm</p>\n\n<h3>Téléphone</h3>\n<p>+237 6XX XXX XXX (Lundi - Vendredi, 8h - 18h)</p>\n\n<h3>Adresse</h3>\n<p>WinPlus SARL<br>Quartier Bastos<br>Yaoundé, Cameroun</p>\n\n<h3>Réseaux sociaux</h3>\n<p>Facebook : @WinPlusCM<br>\nTwitter/X : @WinPlusCM<br>\nInstagram : @winplus.cm<br>\nLinkedIn : WinPlus Cameroun</p>	Contactez WinPlus - Support, partenariats et informations.	contact, support, aide, winplus, cameroun	t	2026-01-20 08:00:00+00	2026-02-20 15:00:15.141262+00	2026-02-20 15:00:15.141262+00	1	\N
\.


--
-- Data for Name: PasswordResetTokens; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PasswordResetTokens" ("Id", "UserId", "Token", "ExpiresAt", "IsUsed", "UsedAt", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: PricingPlans; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."PricingPlans" ("Id", "Name", "Category", "Price", "Period", "Features", "IsPopular", "Icon", "Description", "CreatedAt", "UpdatedAt", "IsDeleted") FROM stdin;
1	Starter	students	0.00	/mois	["Accès 3 cours gratuits","Support communauté","Certificats basiques"]	f	\N	Parfait pour commencer	2026-02-18 13:09:02.595677+00	\N	f
2	Standard	students	9.99	/mois	["Accès 20 cours","Support email","Certificats officiels","Quiz illimités"]	f	\N	Pour les étudiants réguliers	2026-02-18 13:09:02.595677+00	\N	f
3	Premium	students	19.99	/mois	["Accès illimité","Support prioritaire","Certificats officiels","Quiz illimités","Sessions live","Mentor dédié"]	t	\N	Le meilleur pour réussir	2026-02-18 13:09:02.595677+00	\N	f
4	Annuel	students	149.99	/an	["Tout Premium","2 mois offerts","Accès offline","Ressources exclusives"]	f	\N	Économisez 38% par an	2026-02-18 13:09:02.595677+00	\N	f
5	Basique	teachers	0.00	/mois	["Créer 2 cours","50 étudiants max","Analytics basiques"]	f	\N	Pour débuter l'enseignement	2026-02-18 13:09:02.595677+00	\N	f
6	Pro	teachers	29.99	/mois	["Cours illimités","500 étudiants","Analytics avancés","Monétisation"]	t	\N	Pour les enseignants actifs	2026-02-18 13:09:02.595677+00	\N	f
7	Expert	teachers	59.99	/mois	["Tout Pro","Étudiants illimités","Support dédié","Webinaires","Certification WinPlus"]	f	\N	Pour les experts reconnus	2026-02-18 13:09:02.595677+00	\N	f
8	Famille	parents	14.99	/mois	["2 enfants","Suivi détaillé","Rapports hebdo","Support email"]	f	\N	Idéal pour la famille	2026-02-18 13:09:02.595677+00	\N	f
9	Famille+	parents	24.99	/mois	["4 enfants","Suivi en temps réel","Rapports quotidiens","Support prioritaire","Réunions virtuelles"]	t	\N	Pour les grandes familles	2026-02-18 13:09:02.595677+00	\N	f
10	VIP	parents	44.99	/mois	["Enfants illimités","Suivi personnalisé","Coach dédié","Accès tous cours","Bilan mensuel"]	f	\N	Accompagnement premium	2026-02-18 13:09:02.595677+00	\N	f
\.


--
-- Data for Name: Quizzes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Quizzes" ("Id", "Title", "SubjectId", "CreatedBy", "Difficulty", "QuestionCount", "TimeLimit", "PassingScore", "Questions", "IsPublished", "AttemptCount", "AverageScore", "CreatedAt", "UpdatedAt", "IsDeleted") FROM stdin;
1	Quiz - Limites et Continuité	1	3	medium	10	30	60	[{"id": "q1", "options": ["0", "1", "2", "∞"], "question": "Quelle est la limite de (x²-1)/(x-1) quand x tend vers 1 ?", "explanation": "On factorise: (x-1)(x+1)/(x-1) = x+1, donc la limite est 2.", "correctAnswer": "2"}, {"id": "q2", "options": ["Oui, toujours", "Non, jamais", "Seulement si f est dérivable", "Seulement si [a,b] est fermé borné"], "question": "Une fonction continue sur [a,b] atteint-elle toujours ses bornes ?", "explanation": "Théorème des bornes atteintes pour une fonction continue sur un fermé borné.", "correctAnswer": "Oui, toujours"}, {"id": "q3", "options": ["1/x", "x", "ln(x)/x", "1/x²"], "question": "Quelle est la dérivée de ln(x) ?", "explanation": "La dérivée de ln(x) est 1/x pour x > 0.", "correctAnswer": "1/x"}, {"id": "q4", "options": ["Oui", "Non", "Seulement à droite", "Indéterminé"], "question": "La fonction f(x)=|x| est-elle dérivable en 0 ?", "explanation": "La fonction valeur absolue a un point anguleux en 0.", "correctAnswer": "Non"}, {"id": "q5", "options": ["0", "1", "∞", "N'existe pas"], "question": "Quelle est la limite de sin(x)/x quand x→0 ?", "explanation": "C'est une limite classique fondamentale.", "correctAnswer": "1"}, {"id": "q6", "options": ["Un maximum", "Un minimum", "Un point critique", "Un point d'inflexion"], "question": "Si f est dérivable et f'(a)=0, alors a est:", "explanation": "f'(a)=0 signifie que a est un point critique, pas forcément un extremum.", "correctAnswer": "Un point critique"}, {"id": "q7", "options": ["Continue", "Discontinue", "Dérivable", "Intégrable"], "question": "La composée de deux fonctions continues est:", "explanation": "La composée de fonctions continues est continue.", "correctAnswer": "Continue"}, {"id": "q8", "options": ["sin(x)+C", "-sin(x)+C", "cos(x)+C", "tan(x)+C"], "question": "Quelle est la primitive de cos(x) ?", "explanation": "La dérivée de sin(x) est cos(x), donc la primitive de cos(x) est sin(x)+C.", "correctAnswer": "sin(x)+C"}, {"id": "q9", "options": ["0", "1", "+∞", "Dépend de n"], "question": "lim(x→+∞) e^x/x^n = ?", "explanation": "L'exponentielle domine toujours les polynômes à l'infini.", "correctAnswer": "+∞"}, {"id": "q10", "options": ["Oui", "Non", "Seulement si bornée", "Seulement sur un ouvert"], "question": "Une fonction dérivable est-elle forcément continue ?", "explanation": "La dérivabilité implique la continuité.", "correctAnswer": "Oui"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f
2	Quiz - Lois de Newton	2	3	easy	8	20	60	[{"id": "q1", "options": ["Principe d'inertie", "Principe d'action-réaction", "PFD", "Principe de conservation"], "question": "La première loi de Newton est aussi appelée:", "explanation": "La 1ère loi de Newton est le principe d'inertie.", "correctAnswer": "Principe d'inertie"}, {"id": "q2", "options": ["1ère loi", "2ème loi", "3ème loi", "Loi de gravitation"], "question": "F = ma est la:", "explanation": "Le Principe Fondamental de la Dynamique (PFD) est la 2ème loi.", "correctAnswer": "2ème loi"}, {"id": "q3", "options": ["Joule", "Newton", "Pascal", "Watt"], "question": "L'unité de force dans le SI est:", "explanation": "Le Newton (N) est l'unité de force: 1N = 1kg·m/s².", "correctAnswer": "Newton"}, {"id": "q4", "options": ["Une force extérieure agit", "Il a de la masse", "Il est sur Terre", "Il est en contact avec un autre objet"], "question": "Un objet au repos reste au repos sauf si:", "explanation": "C'est le principe d'inertie (1ère loi de Newton).", "correctAnswer": "Une force extérieure agit"}, {"id": "q5", "options": ["8.9 m/s²", "9.81 m/s²", "10.2 m/s²", "6.67 m/s²"], "question": "L'accélération gravitationnelle sur Terre est environ:", "explanation": "g ≈ 9.81 m/s² à la surface de la Terre.", "correctAnswer": "9.81 m/s²"}, {"id": "q6", "options": ["Double", "Est divisée par 2", "Reste la même", "Quadruple"], "question": "Si la masse double et la force reste constante, l'accélération:", "explanation": "a = F/m, si m double, a est divisée par 2.", "correctAnswer": "Est divisée par 2"}, {"id": "q7", "options": ["Égales et opposées", "Égales et de même sens", "Différentes", "Proportionnelles"], "question": "Action et réaction sont:", "explanation": "3ème loi de Newton: les forces sont égales en norme et de sens opposé.", "correctAnswer": "Égales et opposées"}, {"id": "q8", "options": ["5 N", "49 N", "50 N", "500 N"], "question": "Le poids d'un objet de 5kg sur Terre est environ:", "explanation": "P = mg = 5 × 9.81 ≈ 49 N.", "correctAnswer": "49 N"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f
3	Quiz - Techniques de Dissertation	3	7	medium	8	25	60	[{"id": "q1", "options": ["Thèse, Antithèse, Synthèse", "Introduction, Développement, Conclusion", "Cause, Conséquence, Solution", "Chronologique uniquement"], "question": "Un plan dialectique comprend:", "explanation": "Le plan dialectique oppose une thèse à son antithèse puis propose une synthèse.", "correctAnswer": "Thèse, Antithèse, Synthèse"}, {"id": "q2", "options": ["Résumer le texte", "Capter l'attention du lecteur", "Donner la réponse", "Citer l'auteur obligatoirement"], "question": "L'accroche d'une introduction doit:", "explanation": "L'accroche est la première phrase qui attire le lecteur.", "correctAnswer": "Capter l'attention du lecteur"}, {"id": "q3", "options": ["La question à laquelle répond la dissertation", "Le titre du sujet", "La première partie", "La conclusion"], "question": "La problématique est:", "explanation": "La problématique est la question centrale qui guide toute la réflexion.", "correctAnswer": "La question à laquelle répond la dissertation"}, {"id": "q4", "options": ["Relier les idées entre elles", "Décorer le texte", "Remplacer la ponctuation", "Citer un auteur"], "question": "Un connecteur logique sert à:", "explanation": "Les connecteurs assurent la cohérence et la progression du raisonnement.", "correctAnswer": "Relier les idées entre elles"}, {"id": "q5", "options": ["1", "2", "3", "4"], "question": "Combien de parties minimum dans un développement ?", "explanation": "Un développement comporte au minimum 2 parties pour confronter des idées.", "correctAnswer": "2"}, {"id": "q6", "options": ["Répéter l'introduction", "Répondre à la problématique et ouvrir", "Ajouter de nouveaux arguments", "Citer tous les auteurs"], "question": "La conclusion doit:", "explanation": "La conclusion synthétise la réponse et propose une ouverture.", "correctAnswer": "Répondre à la problématique et ouvrir"}, {"id": "q7", "options": ["N'importe quelle source", "D'une œuvre étudiée en rapport avec le sujet", "De Wikipedia", "D'un film uniquement"], "question": "Un exemple littéraire pertinent provient de:", "explanation": "Les exemples doivent être issus d'œuvres littéraires en lien avec le sujet.", "correctAnswer": "D'une œuvre étudiée en rapport avec le sujet"}, {"id": "q8", "options": ["Remplir le texte", "Assurer la progression logique", "Citer un nouveau texte", "Changer de sujet"], "question": "La transition entre deux parties sert à:", "explanation": "La transition résume la partie précédente et annonce la suivante.", "correctAnswer": "Assurer la progression logique"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f
4	Quiz - Python Fondamentaux	9	11	easy	10	20	60	[{"id": "q1", "options": ["var x = 5", "int x = 5", "x = 5", "let x = 5"], "question": "Comment déclarer une variable en Python ?", "explanation": "Python utilise le typage dynamique, pas besoin de déclarer le type.", "correctAnswer": "x = 5"}, {"id": "q2", "options": ["3.33", "3", "4", "1"], "question": "Quel est le résultat de 10 // 3 ?", "explanation": "// est la division entière en Python.", "correctAnswer": "3"}, {"id": "q3", "options": ["list = ()", "list = []", "list = {}", "list = <>"], "question": "Comment créer une liste en Python ?", "explanation": "Les crochets [] créent une liste en Python.", "correctAnswer": "list = []"}, {"id": "q4", "options": ["echo()", "console.log()", "print()", "write()"], "question": "Quelle fonction affiche du texte ?", "explanation": "print() est la fonction d'affichage en Python.", "correctAnswer": "print()"}, {"id": "q5", "options": ["// commentaire", "/* commentaire */", "# commentaire", "-- commentaire"], "question": "Comment écrire un commentaire ?", "explanation": "Le # est utilisé pour les commentaires en Python.", "correctAnswer": "# commentaire"}, {"id": "q6", "options": ["function", "func", "def", "fn"], "question": "Quel mot-clé définit une fonction ?", "explanation": "Le mot-clé def définit une fonction en Python.", "correctAnswer": "def"}, {"id": "q7", "options": ["typeof(x)", "type(x)", "x.type", "getType(x)"], "question": "Comment vérifier le type d'une variable ?", "explanation": "type() retourne le type d'un objet en Python.", "correctAnswer": "type(x)"}, {"id": "q8", "options": ["1", "2", "3", "4"], "question": "Quel est le résultat de len([1,2,3]) ?", "explanation": "len() retourne le nombre d'éléments dans une liste.", "correctAnswer": "3"}, {"id": "q9", "options": ["str1 + str2", "str1 & str2", "str1.concat(str2)", "concat(str1, str2)"], "question": "Comment concaténer deux strings ?", "explanation": "L'opérateur + concatène les chaînes en Python.", "correctAnswer": "str1 + str2"}, {"id": "q10", "options": ["for i in range(10):", "for(i=0;i<10;i++)", "foreach i in 10", "loop i to 10"], "question": "Quelle structure pour une boucle itérative ?", "explanation": "Python utilise for...in avec range() pour itérer.", "correctAnswer": "for i in range(10):"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f
5	Quiz - English Basics	5	15	easy	8	15	60	[{"id": "q1", "options": ["Goed", "Went", "Gone", "Going"], "question": "What is the past tense of 'go' ?", "explanation": "Go is an irregular verb: go → went → gone.", "correctAnswer": "Went"}, {"id": "q2", "options": ["She don't like coffee", "She doesn't likes coffee", "She doesn't like coffee", "She not like coffee"], "question": "Choose the correct sentence:", "explanation": "Third person singular uses doesn't + base form.", "correctAnswer": "She doesn't like coffee"}, {"id": "q3", "options": ["Present Simple", "Present Perfect", "Present Perfect Continuous", "Past Simple"], "question": "'I have been studying for 3 hours' is in which tense?", "explanation": "Have been + -ing indicates ongoing action from past to present.", "correctAnswer": "Present Perfect Continuous"}, {"id": "q4", "options": ["Childs", "Childes", "Children", "Childrens"], "question": "What is the plural of 'child' ?", "explanation": "Child → children is an irregular plural.", "correctAnswer": "Children"}, {"id": "q5", "options": ["I like dogs", "If it rains, I will stay home", "She is tall", "They went to school"], "question": "Which is a conditional sentence?", "explanation": "If-clause + result clause forms a conditional sentence.", "correctAnswer": "If it rains, I will stay home"}, {"id": "q6", "options": ["Noun", "Verb", "Adjective", "Adverb"], "question": "'Beautiful' is what type of word?", "explanation": "Beautiful describes a noun, making it an adjective.", "correctAnswer": "Adjective"}, {"id": "q7", "options": ["in", "at", "on", "by"], "question": "Choose the correct preposition: 'I arrived ___ Monday'", "explanation": "We use 'on' with days of the week.", "correctAnswer": "on"}, {"id": "q8", "options": ["Rare", "Present everywhere", "Fast", "Invisible"], "question": "What does 'ubiquitous' mean?", "explanation": "Ubiquitous means found or existing everywhere.", "correctAnswer": "Present everywhere"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f
6	Quiz - React Hooks	26	11	hard	8	25	60	[{"id": "q1", "options": ["useEffect", "useState", "useContext", "useReducer"], "question": "Quel hook gère l'état local d'un composant ?", "explanation": "useState retourne une paire [valeur, setter] pour gérer l'état local.", "correctAnswer": "useState"}, {"id": "q2", "options": ["Avant le rendu", "Après le rendu", "Pendant le rendu", "Uniquement au montage"], "question": "useEffect s'exécute:", "explanation": "useEffect s'exécute après chaque rendu (par défaut).", "correctAnswer": "Après le rendu"}, {"id": "q3", "options": ["Utiliser useCallback", "Passer un tableau de dépendances", "Utiliser useMemo", "Appeler useEffect deux fois"], "question": "Comment éviter un re-render inutile avec useEffect ?", "explanation": "Le tableau de dépendances contrôle quand l'effet se re-exécute.", "correctAnswer": "Passer un tableau de dépendances"}, {"id": "q4", "options": ["Gérer le state", "Accéder aux éléments DOM", "Créer des routes", "Gérer le CSS"], "question": "useRef est utile pour:", "explanation": "useRef crée une référence persistante, souvent utilisée pour accéder au DOM.", "correctAnswer": "Accéder aux éléments DOM"}, {"id": "q5", "options": ["useState", "useReducer", "useContext", "useCallback"], "question": "Quel hook remplace Redux pour des states complexes ?", "explanation": "useReducer gère des états complexes avec un pattern reducer (action/dispatch).", "correctAnswer": "useReducer"}, {"id": "q6", "options": ["Optimiser les performances", "Partager des données sans prop drilling", "Créer des effets de bord", "Mémoriser des calculs"], "question": "useContext sert à:", "explanation": "useContext permet d'accéder au contexte sans passer les props manuellement.", "correctAnswer": "Partager des données sans prop drilling"}, {"id": "q7", "options": ["Mémoriser un calcul coûteux", "Gérer les effets", "Créer des refs", "Gérer le routing"], "question": "useMemo est utilisé pour:", "explanation": "useMemo met en cache le résultat d'un calcul entre les rendus.", "correctAnswer": "Mémoriser un calcul coûteux"}, {"id": "q8", "options": ["Les appeler dans des boucles", "Les appeler au top level du composant", "Les appeler dans des conditions", "Les appeler dans des callbacks"], "question": "Les règles des Hooks imposent de:", "explanation": "Les Hooks doivent être appelés au plus haut niveau, jamais dans des boucles ou conditions.", "correctAnswer": "Les appeler au top level du composant"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f
7	Quiz - Système Nerveux	8	3	medium	8	20	60	[{"id": "q1", "options": ["Globule rouge", "Neurone", "Plaquette", "Lymphocyte"], "question": "Quelle cellule transmet l'influx nerveux ?", "explanation": "Le neurone est la cellule fondamentale du système nerveux.", "correctAnswer": "Neurone"}, {"id": "q2", "options": ["Un os", "La jonction entre deux neurones", "Un muscle", "Une glande"], "question": "La synapse est:", "explanation": "La synapse permet la transmission du signal entre neurones.", "correctAnswer": "La jonction entre deux neurones"}, {"id": "q3", "options": ["Système nerveux périphérique", "Système nerveux central", "Système endocrinien", "Système digestif"], "question": "Le cerveau fait partie du:", "explanation": "Le SNC comprend le cerveau et la moelle épinière.", "correctAnswer": "Système nerveux central"}, {"id": "q4", "options": ["Du noyau", "De l'axone", "De la synapse", "Des dendrites"], "question": "Les neurotransmetteurs sont libérés au niveau:", "explanation": "Les neurotransmetteurs sont libérés dans la fente synaptique.", "correctAnswer": "De la synapse"}, {"id": "q5", "options": ["1 m/s", "10 m/s", "Jusqu'à 120 m/s", "1000 m/s"], "question": "L'influx nerveux se propage à quelle vitesse ?", "explanation": "Les fibres myélinisées peuvent conduire l'influx jusqu'à 120 m/s.", "correctAnswer": "Jusqu'à 120 m/s"}, {"id": "q6", "options": ["Nourrir le neurone", "Accélérer la conduction nerveuse", "Produire des hormones", "Détruire les virus"], "question": "La myéline sert à:", "explanation": "La gaine de myéline isole l'axone et accélère la propagation du signal.", "correctAnswer": "Accélérer la conduction nerveuse"}, {"id": "q7", "options": ["Les mouvements volontaires", "Les fonctions involontaires", "La pensée", "La mémoire"], "question": "Le système nerveux autonome contrôle:", "explanation": "Le SNA gère le rythme cardiaque, la digestion, etc. de façon involontaire.", "correctAnswer": "Les fonctions involontaires"}, {"id": "q8", "options": ["10", "12", "24", "31"], "question": "Combien de paires de nerfs crâniens ?", "explanation": "Il existe 12 paires de nerfs crâniens.", "correctAnswer": "12"}]	t	0	0.00	2026-02-18 14:55:33.535936+00	2026-02-18 14:55:33.535936+00	f
\.


--
-- Data for Name: RefreshTokens; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."RefreshTokens" ("Id", "UserId", "Token", "ExpiresAt", "RevokedAt", "CreatedAt", "RevokedByIp") FROM stdin;
1	1	eE+DTLXf99h20qsao/MtzD3KsIFV0/Pn5/6mYRcMLnWEPwyUk0cussDiduu5oiU7im3vmDTDPQVH+22Dy6S1OQ==	2026-02-26 16:39:34.633528+00	\N	2026-02-19 16:39:34.633552+00	\N
2	1	4K4XwoHcAoaTaoX4yx7zMRR5sFLnF5vY3elL4WcBjtT0ddhEO3Mi136cQZiy5KoGOlAmCGl4lx1jWHUlGPQIRw==	2026-02-26 16:46:44.90183+00	\N	2026-02-19 16:46:44.901831+00	\N
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
-- Data for Name: Revisions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Revisions" ("Id", "Title", "SubjectId", "CreatedBy", "Content", "Summary", "KeyPoints", "DocumentUrl", "TopicCount", "Difficulty", "EstimatedDuration", "ViewCount", "IsPublished", "CreatedAt", "UpdatedAt", "IsDeleted") FROM stdin;
1	Fiche de révision - Limites	1	3	Rappel complet sur les limites de fonctions: définitions, théorèmes fondamentaux, formes indéterminées et méthodes de levée.	Maîtriser les techniques de calcul de limites pour le Bac.	["Définition epsilon-delta", "Limites usuelles", "Formes indéterminées 0/0, ∞/∞, ∞-∞", "Théorème des gendarmes", "Croissances comparées"]	https://docs.winplus.cm/revisions/maths/limites.pdf	5	medium	45	234	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
2	Fiche de révision - Dérivées et Applications	1	3	Dérivation: règles, dérivées composées, étude de fonctions, tangentes et optimisation.	Tout sur les dérivées: du calcul à l'application.	["Règles de dérivation", "Dérivée d'une composée", "Tableau de variation", "Tangente en un point", "Problèmes d'optimisation"]	https://docs.winplus.cm/revisions/maths/derivees.pdf	5	medium	50	312	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
3	Fiche de révision - Intégrales	1	3	Intégration: primitives, intégrale définie, calcul d'aires et méthodes d'intégration.	Des primitives au calcul d'aires.	["Primitives usuelles", "Intégrale de Riemann", "Calcul d'aires", "Intégration par parties", "Changement de variable"]	https://docs.winplus.cm/revisions/maths/integrales.pdf	5	difficile	55	189	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
4	Fiche de révision - Mécanique Newtonienne	2	3	Les 3 lois de Newton, applications aux mouvements rectilignes et circulaires, chute libre.	Toute la mécanique du programme de Terminale.	["3 lois de Newton", "Mouvement rectiligne uniforme", "Chute libre", "Mouvement circulaire", "Travail et énergie"]	https://docs.winplus.cm/revisions/physique/mecanique.pdf	5	medium	40	198	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
5	Fiche de révision - Énergie	2	3	Conservation de l'énergie, énergie cinétique, potentielle, mécanique. Travail d'une force.	Comprendre et appliquer la conservation de l'énergie.	["Énergie cinétique Ec=½mv²", "Énergie potentielle Ep=mgh", "Théorème de l'énergie cinétique", "Conservation énergie mécanique", "Puissance et rendement"]	https://docs.winplus.cm/revisions/physique/energie.pdf	5	medium	35	167	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
6	Fiche de révision - Méthodologie Dissertation	3	7	Guide complet pour réussir la dissertation au Bac: de l'analyse du sujet à la rédaction finale.	La méthode complète pour la dissertation.	["Analyser le sujet", "Construire la problématique", "Choisir un plan", "Rédiger l'introduction", "Construire des paragraphes argumentés", "Rédiger la conclusion"]	https://docs.winplus.cm/revisions/francais/methodo-dissert.pdf	6	medium	40	456	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
7	Fiche de révision - Figures de Style	3	7	Toutes les figures de style au programme: définition, exemples et exercices.	Reconnaître et analyser les figures de style.	["Métaphore et comparaison", "Hyperbole et litote", "Anaphore et épiphore", "Chiasme et antithèse", "Oxymore et paradoxe"]	https://docs.winplus.cm/revisions/francais/figures-style.pdf	5	facile	30	345	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
8	Revision Sheet - English Tenses	5	15	Complete review of all English tenses with usage rules and examples.	Master all English tenses in one sheet.	["Present Simple vs Continuous", "Past Simple vs Perfect", "Future tenses", "Conditional tenses", "Passive voice"]	https://docs.winplus.cm/revisions/anglais/tenses.pdf	5	medium	35	289	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
9	Revision Sheet - Common Mistakes	5	15	Most frequent errors by French speakers learning English and how to fix them.	Avoid the most common errors in English.	["False friends", "Preposition errors", "Articles (a/an/the)", "Word order", "Pronunciation traps"]	https://docs.winplus.cm/revisions/anglais/common-mistakes.pdf	5	facile	25	234	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
10	Fiche de révision - Python Essentiel	9	11	Résumé complet de Python: syntaxe, structures de données, fonctions, modules et bonnes pratiques.	Tout Python en une fiche.	["Variables et types", "Listes, tuples, dictionnaires", "Fonctions et lambda", "Modules et imports", "Gestion d'erreurs try/except"]	https://docs.winplus.cm/revisions/python/essentiel.pdf	5	facile	30	534	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
11	Fiche de révision - Algorithmes Classiques	9	11	Les algorithmes fondamentaux à connaître: tri, recherche, récursivité.	Les algorithmes incontournables.	["Tri à bulles", "Tri par sélection", "Recherche dichotomique", "Récursivité", "Complexité algorithmique"]	https://docs.winplus.cm/revisions/python/algorithmes.pdf	5	medium	40	312	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
12	Fiche de révision - React Hooks	26	11	Guide complet des React Hooks: useState, useEffect, useContext, useReducer, useMemo, useCallback.	Maîtriser tous les Hooks React.	["useState - état local", "useEffect - effets de bord", "useContext - contexte global", "useReducer - état complexe", "useMemo/useCallback - optimisation"]	https://docs.winplus.cm/revisions/react/hooks.pdf	5	difficile	45	289	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
13	Fiche de révision - Système Nerveux	8	3	Le système nerveux humain: SNC, SNP, neurones, synapses et transmission nerveuse.	Comprendre le fonctionnement du système nerveux.	["Structure du neurone", "Potentiel d'action", "Transmission synaptique", "SNC vs SNP", "Système nerveux autonome"]	https://docs.winplus.cm/revisions/bio/systeme-nerveux.pdf	5	medium	40	198	t	2026-02-18 14:55:43.416278+00	2026-02-18 14:55:43.416278+00	f
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
-- Data for Name: Subjects; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Subjects" ("Id", "Title", "Description", "Category", "ThumbnailUrl", "Price", "IsPublished", "EnrollmentCount", "AverageRating", "TotalRatings", "CreatedAt", "UpdatedAt", "IsDeleted", "IsFeatured", "Tags") FROM stdin;
1	Mathématiques Terminale S	Cours complet de maths pour la terminale scientifique	Mathématiques	https://images.unsplash.com/photo-1635070041078-e363dbe005cb?w=400	29.99	t	245	4.80	89	2025-11-10 13:08:28.636855+00	\N	f	t	["mathématiques", "limites", "dérivées", "intégrales", "terminale", "bac"]
2	Physique-Chimie Terminale	Maîtrisez la physique et la chimie pour le bac	Sciences	https://images.unsplash.com/photo-1532187863486-abf9dbad1b69?w=400	24.99	t	189	4.60	72	2025-11-12 13:08:28.636855+00	\N	f	t	["physique", "chimie", "mécanique", "énergie", "terminale", "bac"]
3	Français Dissertation	Techniques de dissertation et analyse littéraire	Lettres	https://images.unsplash.com/photo-1456513080510-7bf3a84b82f8?w=400	19.99	t	312	4.70	105	2025-11-14 13:08:28.636855+00	\N	f	t	["français", "dissertation", "commentaire", "littérature", "bac"]
4	Histoire-Géographie Bac	Révisions complètes HG pour le baccalauréat	Histoire	https://images.unsplash.com/photo-1524995997946-a1c2e315a42f?w=400	22.99	t	156	4.50	61	2025-11-16 13:08:28.636855+00	\N	f	f	["histoire", "géographie", "géopolitique", "bac"]
5	Anglais Conversationnel	Parlez anglais avec confiance et fluidité	Langues	https://images.unsplash.com/photo-1434030216411-0b793f4b6174?w=400	34.99	t	428	4.90	187	2025-11-18 13:08:28.636855+00	\N	f	t	["anglais", "conversation", "oral", "grammaire", "vocabulaire"]
6	Espagnol Débutant	Apprenez les bases de l'espagnol facilement	Langues	https://images.unsplash.com/photo-1551818255-e6e10975bc17?w=400	19.99	t	203	4.40	78	2025-11-20 13:08:28.636855+00	\N	f	f	["espagnol", "débutant", "langue", "grammaire"]
7	Philosophie Bac	Méthodes et notions clés pour l'épreuve de philo	Philosophie	https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=400	17.99	t	134	4.30	49	2025-11-22 13:08:28.636855+00	\N	f	f	["philosophie", "dissertation", "logique", "éthique", "bac"]
8	Biologie Humaine	Comprendre le corps humain et ses fonctions	Sciences	https://images.unsplash.com/photo-1559757148-5c350d0d3c56?w=400	27.99	t	267	4.70	93	2025-11-24 13:08:28.636855+00	\N	f	t	["biologie", "anatomie", "cellule", "système nerveux", "svt"]
9	Informatique Python	Programmation Python de zéro à avancé	Informatique	https://images.unsplash.com/photo-1526374965328-7f61d4dc18c5?w=400	39.99	t	534	4.90	234	2025-11-26 13:08:28.636855+00	\N	f	t	["python", "programmation", "algorithme", "code", "informatique"]
10	Économie & Gestion	Introduction à l'économie et gestion d'entreprise	Économie	https://images.unsplash.com/photo-1611974789855-9c2a0a7236a3?w=400	24.99	t	178	4.50	67	2025-11-28 13:08:28.636855+00	\N	f	f	["économie", "gestion", "entreprise", "marché"]
11	Algèbre Linéaire L1	Vecteurs, matrices et espaces vectoriels pour le supérieur	Mathématiques	https://images.unsplash.com/photo-1509228468518-180dd4864904?w=400	34.99	t	145	4.60	54	2025-11-30 13:08:28.636855+00	\N	f	f	["algèbre", "linéaire", "matrices", "espaces vectoriels", "L1"]
12	Chimie Organique	Réactions et mécanismes de chimie organique	Sciences	https://images.unsplash.com/photo-1628863353691-0071c8c1574d?w=400	29.99	t	112	4.40	41	2025-12-02 13:08:28.636855+00	\N	f	f	["chimie", "organique", "molécules", "réactions"]
13	Littérature Française	Les grands auteurs et œuvres de la littérature française	Lettres	https://images.unsplash.com/photo-1481627834876-b7833e8f5570?w=400	19.99	t	189	4.60	72	2025-12-04 13:08:28.636855+00	\N	f	f	["littérature", "française", "roman", "poésie", "théâtre"]
14	Géopolitique Mondiale	Comprendre les enjeux géopolitiques contemporains	Histoire	https://images.unsplash.com/photo-1521295121783-8a321d551ad2?w=400	22.99	t	98	4.30	36	2025-12-06 13:08:28.636855+00	\N	f	f	["géopolitique", "relations internationales", "conflits"]
16	Statistiques & Probabilités	Maîtrisez les stats et probas pour les études supérieures	Mathématiques	https://images.unsplash.com/photo-1551288049-bebda4e38f71?w=400	29.99	t	167	4.50	62	2025-12-10 13:08:28.636855+00	\N	f	f	["statistiques", "probabilités", "loi normale", "échantillonnage"]
17	Éthique & Philosophie Moderne	Les grands courants philosophiques du XXe siècle	Philosophie	https://images.unsplash.com/photo-1456324504439-367cee3b3c32?w=400	17.99	t	76	4.10	28	2025-12-12 13:08:28.636855+00	\N	f	f	["éthique", "philosophie", "moderne", "morale"]
18	Anatomie & Physiologie	Cours d'anatomie pour étudiants en santé	Sciences	https://images.unsplash.com/photo-1530026405186-ed1f139313f8?w=400	34.99	t	223	4.70	84	2025-12-14 13:08:28.636855+00	\N	f	t	["anatomie", "physiologie", "corps humain", "organes"]
20	Marketing Digital	Stratégies de marketing en ligne pour entreprises	Économie	https://images.unsplash.com/photo-1432888622747-4eb9a8f5a07d?w=400	29.99	t	298	4.60	112	2025-12-18 13:08:28.636855+00	\N	f	t	["marketing", "digital", "seo", "réseaux sociaux", "stratégie"]
21	Calcul Intégral	Intégrales simples, doubles et applications	Mathématiques	https://images.unsplash.com/photo-1596495578065-6e0763fa1178?w=400	32.99	t	134	4.50	50	2025-12-20 13:08:28.636855+00	\N	f	f	["calcul", "intégral", "primitives", "aires"]
22	Thermodynamique	Lois de la thermodynamique et applications industrielles	Sciences	https://images.unsplash.com/photo-1581093588401-fbb62a02f120?w=400	27.99	t	89	4.30	33	2025-12-22 13:08:28.636855+00	\N	f	f	["thermodynamique", "chaleur", "entropie", "gaz parfait"]
23	Comptabilité Générale	Bases de la comptabilité pour entreprises et étudiants	Économie	https://images.unsplash.com/photo-1554224155-6726b3ff858f?w=400	24.99	t	156	4.40	58	2025-12-24 13:08:28.636855+00	\N	f	f	["comptabilité", "bilan", "résultat", "écriture comptable"]
25	Psychologie Cognitive	Comprendre les mécanismes de la pensée et de la mémoire	Sciences	https://images.unsplash.com/photo-1559757175-0eb30cd8c063?w=400	29.99	t	198	4.60	75	2025-12-28 13:08:28.636855+00	\N	f	f	["psychologie", "cognitive", "mémoire", "perception", "attention"]
26	React & Next.js	Développez des applications web modernes avec React	Informatique	https://images.unsplash.com/photo-1633356122544-f134324a6cee?w=400	49.99	t	445	4.90	198	2025-12-30 13:08:28.636855+00	\N	f	t	["react", "nextjs", "hooks", "composants", "frontend"]
27	Droit Civil Fondamental	Introduction au droit civil français et ses applications	Droit	https://images.unsplash.com/photo-1589829545856-d10d557cf95f?w=400	27.99	t	112	4.40	42	2026-01-01 13:08:28.636855+00	\N	f	f	["droit", "civil", "contrat", "obligations", "responsabilité"]
29	Dessin & Arts Plastiques	Techniques de dessin et expression artistique	Arts	https://images.unsplash.com/photo-1513364776144-60967b0f800f?w=400	22.99	t	134	4.30	49	2026-01-05 13:08:28.636855+00	\N	f	f	["dessin", "arts", "plastiques", "peinture", "créativité"]
30	Préparation TOEFL	Réussissez le TOEFL avec méthodes et exercices complets	Langues	https://images.unsplash.com/photo-1546410531-bb4caa6b424d?w=400	39.99	t	267	4.70	98	2026-01-07 13:08:28.636855+00	\N	f	t	["toefl", "préparation", "anglais", "test", "certification"]
15	Allemand Intermédiaire	Progressez en allemand niveau B1-B2	Langues	https://images.unsplash.com/photo-1467269204594-9661b134dd2b?w=400	24.99	t	87	4.20	31	2025-12-08 13:08:28.636855+00	\N	f	f	["allemand", "intermédiaire", "langue", "grammaire"]
19	JavaScript Avancé	Maîtrisez JS moderne, ES6+, async/await et plus	Informatique	https://images.unsplash.com/photo-1627398242454-45a1465c2479?w=400	44.99	t	389	4.80	156	2025-12-16 13:08:28.636855+00	\N	f	t	["javascript", "avancé", "async", "closures", "prototypes"]
24	Latin Débutant	Introduction au latin classique et à la civilisation romaine	Lettres	https://images.unsplash.com/photo-1552664730-d307ca884978?w=400	14.99	t	45	4.00	17	2025-12-26 13:08:28.636855+00	\N	f	f	["latin", "débutant", "langue", "grammaire", "déclinaisons"]
28	Musique & Solfège	Apprenez le solfège et les bases de la théorie musicale	Arts	https://images.unsplash.com/photo-1507838153414-b4b713384a76?w=400	19.99	t	167	4.50	63	2026-01-03 13:08:28.636855+00	\N	f	f	["musique", "solfège", "notes", "rythme", "harmonie"]
\.


--
-- Data for Name: Subscriptions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Subscriptions" ("Id", "UserId", "PricingPlanId", "StartDate", "EndDate", "Status", "CreatedAt", "UpdatedAt") FROM stdin;
1	1	3	2026-01-19 13:10:03.522782+00	2027-01-19 13:10:03.522782+00	active	2026-02-18 13:10:03.522782+00	\N
\.


--
-- Data for Name: TwoFactorTokens; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."TwoFactorTokens" ("Id", "UserId", "TotpSecret", "IsTotpEnabled", "BackupCodesCount", "CreatedAt", "UpdatedAt") FROM stdin;
\.


--
-- Data for Name: UserInterests; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."UserInterests" ("Id", "UserId", "SubjectId", "Interest", "CreatedAt") FROM stdin;
1	1	1	Mathématiques pour le BAC	2026-02-20 15:01:27.530625+00
2	1	9	Programmation Python	2026-02-20 15:01:27.530625+00
3	1	19	JavaScript avancé	2026-02-20 15:01:27.530625+00
4	1	26	Développement React	2026-02-20 15:01:27.530625+00
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
1	1	t	t	t	f	t	t	2026-02-18 13:10:03.51544	2026-02-18 13:10:03.51544
\.


--
-- Data for Name: UserPrivacySettings; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."UserPrivacySettings" ("Id", "UserId", "ProfileVisible", "ShowProgressPublic", "AllowMessages", "AllowFriends", "CreatedAt", "UpdatedAt") FROM stdin;
1	1	t	f	t	t	2026-02-18 13:10:03.517902	2026-02-18 13:10:03.517902
\.


--
-- Data for Name: UserSessions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."UserSessions" ("Id", "UserId", "DeviceName", "DeviceType", "IpAddress", "UserAgent", "Location", "RefreshTokenId", "CreatedAt", "LastActivityAt", "ExpiresAt", "IsActive") FROM stdin;
\.


--
-- Data for Name: UserTwoFactorAuthentication; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."UserTwoFactorAuthentication" ("Id", "UserId", "IsEnabled", "Method", "TotpSecret", "BackupCodes", "BackupCodesUsed", "EnabledAt", "LastVerifiedAt", "CreatedAt", "UpdatedAt") FROM stdin;
1	1	f	\N	\N	\N	0	\N	\N	2026-02-18 13:10:03.520027	2026-02-18 13:10:03.520027
\.


--
-- Data for Name: Users; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Users" ("Id", "CognitoId", "Email", "FirstName", "LastName", "ProfileImageUrl", "Bio", "IsActive", "CreatedAt", "UpdatedAt", "IsEmailVerified", "LastLoginAt", "PasswordHash", "Phone", "Role", "VerifiedAt", "VerificationCode", "VerificationCodeExpiredAt", "AvatarUrl", "DeletedBy", "DeletedByUserId", "EmailChangeToken", "EmailChangeTokenExpiry", "PendingEmail", "IsDeleted", "EmailVerified") FROM stdin;
2	\N	marie.dupont@gmail.com	Marie	DUPONT	\N	Étudiante en mathématiques	t	2025-11-20 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword1	+33611111111	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Marie	\N	\N	\N	\N	\N	f	t
3	\N	jean.martin@gmail.com	Jean	MARTIN	\N	Professeur de physique depuis 10 ans	t	2025-11-25 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword2	+33622222222	teacher	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Jean	\N	\N	\N	\N	\N	f	t
4	\N	sophie.bernard@gmail.com	Sophie	BERNARD	\N	Passionnée de littérature française	t	2025-11-30 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword3	+33633333333	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Sophie	\N	\N	\N	\N	\N	f	t
5	\N	paul.leroy@gmail.com	Paul	LEROY	\N	Parent de 2 enfants scolarisés	t	2025-12-05 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword4	+33644444444	parent	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Paul	\N	\N	\N	\N	\N	f	t
6	\N	camille.moreau@gmail.com	Camille	MOREAU	\N	Lycéenne en terminale S	t	2025-12-10 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword5	+33655555555	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Camille	\N	\N	\N	\N	\N	f	t
7	\N	lucas.petit@gmail.com	Lucas	PETIT	\N	Enseignant en histoire-géographie	t	2025-12-15 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword6	+33666666666	teacher	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Lucas	\N	\N	\N	\N	\N	f	t
8	\N	emma.richard@gmail.com	Emma	RICHARD	\N	Étudiante en médecine L1	t	2025-12-20 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword7	+33677777777	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Emma	\N	\N	\N	\N	\N	f	t
9	\N	thomas.simon@gmail.com	Thomas	SIMON	\N	Papa impliqué dans la scolarité	t	2025-12-25 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword8	+33688888888	parent	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Thomas	\N	\N	\N	\N	\N	f	t
10	\N	lea.lambert@gmail.com	Léa	LAMBERT	\N	Collégienne en 3ème	t	2025-12-30 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword9	+33699999999	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Lea	\N	\N	\N	\N	\N	f	t
11	\N	nicolas.garcia@gmail.com	Nicolas	GARCIA	\N	Prof de SVT et biologie	t	2026-01-04 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword10	+33610101010	teacher	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Nicolas	\N	\N	\N	\N	\N	f	t
12	\N	julie.roux@gmail.com	Julie	ROUX	\N	Mère de famille, suivi scolaire	t	2026-01-09 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword11	+33621212121	parent	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Julie	\N	\N	\N	\N	\N	f	t
13	\N	antoine.fournier@gmail.com	Antoine	FOURNIER	\N	Étudiant en informatique L2	t	2026-01-14 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword12	+33632323232	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Antoine	\N	\N	\N	\N	\N	f	t
14	\N	clara.morel@gmail.com	Clara	MOREL	\N	Passionnée de chimie et sciences	t	2026-01-19 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword13	+33643434343	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Clara	\N	\N	\N	\N	\N	f	t
15	\N	david.nguyen@gmail.com	David	NGUYEN	\N	Professeur de mathématiques sup	t	2026-01-24 13:08:11.265925+00	\N	t	\N	$2a$10$hashedpassword14	+33654545454	teacher	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=David	\N	\N	\N	\N	\N	f	t
1	\N	miguelmoukoko4@gmail.com	Miguel	MOUKOKO	\N	Administrateur de la plateforme WinPlus	t	2026-02-18 12:58:48.411673+00	2026-02-18 13:05:56.466219+00	t	2026-02-19 16:46:44.902092+00	$2a$11$81.Azb/pb7UDWfi9eiW5p.TCBu0NVgRzUKvz.cYQC4MPHyrAMcehi	+237691697924	student	\N	\N	\N	https://api.dicebear.com/7.x/avataaars/svg?seed=Miguel	\N	\N	\N	\N	\N	f	t
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
-- Data for Name: payments; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.payments ("Id", "OrderId", "UserId", "Amount", "Currency", "Status", "PaymentMethod", "TransactionId", "Description", "FeeAmount", "InitiatedAt", "ProcessedAt", "CompletedAt", "ErrorMessage", "RetryCount", "NextRetryAt", "Metadata", "CreatedAt", "UpdatedAt") FROM stdin;
3	1	1	29.99	XAF	completed	mobile_money	TXN-MTN-001	\N	\N	2026-01-10 10:30:00+00	2026-01-10 10:30:30+00	2026-01-10 10:31:00+00	\N	\N	\N	\N	2026-01-10 10:31:00+00	2026-01-10 10:31:00+00
4	2	1	39.99	XAF	completed	mobile_money	TXN-MTN-002	\N	\N	2026-01-12 14:00:00+00	2026-01-12 14:00:30+00	2026-01-12 14:01:00+00	\N	\N	\N	\N	2026-01-12 14:01:00+00	2026-01-12 14:01:00+00
5	3	1	44.99	XAF	completed	orange_money	TXN-OM-001	\N	\N	2026-01-20 09:15:00+00	2026-01-20 09:15:30+00	2026-01-20 09:16:00+00	\N	\N	\N	\N	2026-01-20 09:16:00+00	2026-01-20 09:16:00+00
6	4	2	29.99	XAF	completed	mobile_money	TXN-MTN-003	\N	\N	2025-12-15 11:00:00+00	2025-12-15 11:00:30+00	2025-12-15 11:01:00+00	\N	\N	\N	\N	2025-12-15 11:01:00+00	2025-12-15 11:01:00+00
7	5	2	19.99	XAF	completed	card	TXN-CARD-001	\N	\N	2025-12-18 16:30:00+00	2025-12-18 16:30:30+00	2025-12-18 16:31:00+00	\N	\N	\N	\N	2025-12-18 16:31:00+00	2025-12-18 16:31:00+00
8	6	2	34.99	XAF	completed	mobile_money	TXN-MTN-004	\N	\N	2025-12-20 08:45:00+00	2025-12-20 08:45:30+00	2025-12-20 08:46:00+00	\N	\N	\N	\N	2025-12-20 08:46:00+00	2025-12-20 08:46:00+00
9	7	4	34.99	XAF	completed	orange_money	TXN-OM-002	\N	\N	2026-01-05 13:20:00+00	2026-01-05 13:20:30+00	2026-01-05 13:21:00+00	\N	\N	\N	\N	2026-01-05 13:21:00+00	2026-01-05 13:21:00+00
10	8	4	29.99	XAF	completed	mobile_money	TXN-MTN-005	\N	\N	2026-01-08 10:00:00+00	2026-01-08 10:00:30+00	2026-01-08 10:01:00+00	\N	\N	\N	\N	2026-01-08 10:01:00+00	2026-01-08 10:01:00+00
11	9	5	39.99	XAF	completed	card	TXN-CARD-002	\N	\N	2026-01-15 15:30:00+00	2026-01-15 15:30:30+00	2026-01-15 15:31:00+00	\N	\N	\N	\N	2026-01-15 15:31:00+00	2026-01-15 15:31:00+00
12	10	5	49.99	XAF	completed	mobile_money	TXN-MTN-006	\N	\N	2026-01-18 09:00:00+00	2026-01-18 09:00:30+00	2026-01-18 09:01:00+00	\N	\N	\N	\N	2026-01-18 09:01:00+00	2026-01-18 09:01:00+00
13	11	9	34.99	XAF	completed	orange_money	TXN-OM-003	\N	\N	2026-01-22 11:00:00+00	2026-01-22 11:00:30+00	2026-01-22 11:01:00+00	\N	\N	\N	\N	2026-01-22 11:01:00+00	2026-01-22 11:01:00+00
14	12	9	39.99	XAF	completed	mobile_money	TXN-MTN-007	\N	\N	2026-01-25 14:15:00+00	2026-01-25 14:15:30+00	2026-01-25 14:16:00+00	\N	\N	\N	\N	2026-01-25 14:16:00+00	2026-01-25 14:16:00+00
15	13	12	49.99	XAF	completed	card	TXN-CARD-003	\N	\N	2026-02-01 10:30:00+00	2026-02-01 10:30:30+00	2026-02-01 10:31:00+00	\N	\N	\N	\N	2026-02-01 10:31:00+00	2026-02-01 10:31:00+00
16	14	13	39.99	XAF	completed	mobile_money	TXN-MTN-008	\N	\N	2026-02-05 08:45:00+00	2026-02-05 08:45:30+00	2026-02-05 08:46:00+00	\N	\N	\N	\N	2026-02-05 08:46:00+00	2026-02-05 08:46:00+00
17	15	13	44.99	XAF	completed	orange_money	TXN-OM-004	\N	\N	2026-02-10 16:00:00+00	2026-02-10 16:00:30+00	2026-02-10 16:01:00+00	\N	\N	\N	\N	2026-02-10 16:01:00+00	2026-02-10 16:01:00+00
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

SELECT pg_catalog.setval('public."AnalyticsEvents_Id_seq"', 5, true);


--
-- Name: Announcements_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Announcements_Id_seq"', 5, true);


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
-- Name: CourseContents_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."CourseContents_Id_seq"', 32, true);


--
-- Name: DeviceInfos_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."DeviceInfos_Id_seq"', 1, true);


--
-- Name: EmailVerificationTokens_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."EmailVerificationTokens_Id_seq"', 1, true);


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
-- Name: Favorites_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Favorites_Id_seq"', 10, true);


--
-- Name: Goals_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Goals_Id_seq"', 10, true);


--
-- Name: HomePageFeatures_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."HomePageFeatures_Id_seq"', 6, true);


--
-- Name: Institutions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Institutions_Id_seq"', 25, true);


--
-- Name: LearningHistories_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."LearningHistories_Id_seq"', 36, true);


--
-- Name: Levels_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Levels_Id_seq"', 24, true);


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

SELECT pg_catalog.setval('public."OrderItems_Id_seq"', 1, false);


--
-- Name: Orders_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Orders_Id_seq"', 17, true);


--
-- Name: Pages_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Pages_Id_seq"', 5, true);


--
-- Name: PasswordResetTokens_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."PasswordResetTokens_Id_seq"', 1, false);


--
-- Name: PricingPlans_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."PricingPlans_Id_seq"', 10, true);


--
-- Name: Quizzes_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Quizzes_Id_seq"', 7, true);


--
-- Name: RefreshTokens_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."RefreshTokens_Id_seq"', 2, true);


--
-- Name: Reviews_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Reviews_Id_seq"', 10, true);


--
-- Name: Revisions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Revisions_Id_seq"', 13, true);


--
-- Name: Sessions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Sessions_Id_seq"', 6, true);


--
-- Name: Subjects_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Subjects_Id_seq"', 30, true);


--
-- Name: Subscriptions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Subscriptions_Id_seq"', 1, true);


--
-- Name: TwoFactorTokens_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."TwoFactorTokens_Id_seq"', 1, false);


--
-- Name: UserInterests_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."UserInterests_Id_seq"', 16, true);


--
-- Name: UserNotificationSettings_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."UserNotificationSettings_Id_seq"', 1, true);


--
-- Name: UserPrivacySettings_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."UserPrivacySettings_Id_seq"', 1, true);


--
-- Name: UserSessions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."UserSessions_Id_seq"', 1, true);


--
-- Name: UserTwoFactorAuthentication_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."UserTwoFactorAuthentication_Id_seq"', 1, true);


--
-- Name: Users_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Users_Id_seq"', 15, true);


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

SELECT pg_catalog.setval('public."payments_Id_seq"', 17, true);


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
-- Name: DeviceInfos DeviceInfos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DeviceInfos"
    ADD CONSTRAINT "DeviceInfos_pkey" PRIMARY KEY ("Id");


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
-- Name: Institutions Institutions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Institutions"
    ADD CONSTRAINT "Institutions_pkey" PRIMARY KEY ("Id");


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
-- Name: payments PK_payments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
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
-- Name: PricingPlans PricingPlans_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PricingPlans"
    ADD CONSTRAINT "PricingPlans_pkey" PRIMARY KEY ("Id");


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
-- Name: Subscriptions Subscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Subscriptions"
    ADD CONSTRAINT "Subscriptions_pkey" PRIMARY KEY ("Id");


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
-- Name: IX_CartItems_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CartItems_SubjectId" ON public."CartItems" USING btree ("SubjectId");


--
-- Name: IX_CartItems_UserId_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_CartItems_UserId_SubjectId" ON public."CartItems" USING btree ("UserId", "SubjectId");


--
-- Name: IX_CourseContents_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CourseContents_SubjectId" ON public."CourseContents" USING btree ("SubjectId");


--
-- Name: IX_Enrollments_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Enrollments_SubjectId" ON public."Enrollments" USING btree ("SubjectId");


--
-- Name: IX_Enrollments_UserId_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Enrollments_UserId_SubjectId" ON public."Enrollments" USING btree ("UserId", "SubjectId");


--
-- Name: IX_Favorites_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Favorites_SubjectId" ON public."Favorites" USING btree ("SubjectId");


--
-- Name: IX_Favorites_UserId_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Favorites_UserId_SubjectId" ON public."Favorites" USING btree ("UserId", "SubjectId");


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
-- Name: IX_payments_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payments_OrderId" ON public.payments USING btree ("OrderId");


--
-- Name: IX_payments_TransactionId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payments_TransactionId" ON public.payments USING btree ("TransactionId");


--
-- Name: IX_payments_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payments_UserId" ON public.payments USING btree ("UserId");


--
-- Name: idx_abuse_reports_created; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_abuse_reports_created ON public.abuse_reports USING btree (created_at);


--
-- Name: idx_abuse_reports_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_abuse_reports_status ON public.abuse_reports USING btree (status);


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
-- Name: idx_goals_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_goals_user ON public."Goals" USING btree ("UserId");


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
-- Name: DeviceInfos DeviceInfos_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DeviceInfos"
    ADD CONSTRAINT "DeviceInfos_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


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
-- Name: Notifications FK_Notifications_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Notifications"
    ADD CONSTRAINT "FK_Notifications_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


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
-- Name: payments FK_payments_Orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT "FK_payments_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public."Orders"("Id") ON DELETE CASCADE;


--
-- Name: payments FK_payments_Users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT "FK_payments_Users_UserId" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


--
-- Name: Goals Goals_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Goals"
    ADD CONSTRAINT "Goals_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


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
-- Name: PasswordResetTokens PasswordResetTokens_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."PasswordResetTokens"
    ADD CONSTRAINT "PasswordResetTokens_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


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
-- Name: TwoFactorTokens TwoFactorTokens_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."TwoFactorTokens"
    ADD CONSTRAINT "TwoFactorTokens_UserId_fkey" FOREIGN KEY ("UserId") REFERENCES public."Users"("Id") ON DELETE CASCADE;


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

\unrestrict HL3bY2EiZcN7BH3bgyfcfnfsbcaDAxMkjSwIaFtrGCnQaDGwWN2C6kYTvMJu99Y

