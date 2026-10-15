using System;
using System.Collections.Generic;
using System.Linq;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services.Entitlements;

namespace Backend.Tests
{
    /// <summary>
    /// Grille de départ du 9.7b (étude section 1.3 à 1.5, décision 18.21),
    /// posée en base de test : mêmes valeurs que SQL_Subscriptions97bGrid.sql.
    /// </summary>
    public static class Subscriptions97bTestGrid
    {
        public sealed record Family(int PlanId, int VersionId, string Key);

        public static Dictionary<string, Family> Seed(ApplicationDbContext db)
        {
            var result = new Dictionary<string, Family>();
            void Add(string key, string audience, string name, bool free, int sort, decimal monthly, Dictionary<string, string> values)
            {
                var plan = new PricingPlan
                {
                    Name = name, Category = audience + "s", Audience = audience, PlanKey = key, IsFreeTier = free,
                    SortOrder = sort, Price = monthly, Currency = "XAF",
                };
                var v = new PlanVersion { VersionNumber = 1, Status = PlanVersionStatus.Published, PublishedAt = DateTime.UtcNow.AddDays(-30), ExistingSubscribersFrom = DateTime.UtcNow.AddDays(-30) };
                if (!free)
                {
                    v.Prices.Add(new PlanPrice { Period = "monthly", PriceTtc = monthly });
                    v.Prices.Add(new PlanPrice { Period = "quarterly", PriceTtc = Math.Round(monthly * 3 * 0.9m) });
                    v.Prices.Add(new PlanPrice { Period = "yearly", PriceTtc = Math.Round(monthly * 12 * 0.8m) });
                }
                foreach (var (k, val) in values) v.Entitlements.Add(new PlanEntitlement { Key = k, Value = val });
                plan.Versions.Add(v);
                db.PricingPlans.Add(plan);
                db.SaveChanges();
                result[key] = new Family(plan.Id, v.Id, key);
            }

            Dictionary<string, string> Student(bool catalog, int tokens, bool attach, bool plan, bool session, bool coach, int groups, int fav) => new()
            {
                ["catalog.access"] = catalog ? "true" : "false",
                ["ai.reference_tokens"] = tokens.ToString(),
                ["ai.feature.attachments"] = attach ? "true" : "false",
                ["ai.feature.study_plan"] = plan ? "true" : "false",
                ["ai.feature.study_session"] = session ? "true" : "false",
                ["ai.feature.exam_coach"] = coach ? "true" : "false",
                ["groups.max_owned"] = groups.ToString(),
                ["favorites.max"] = fav.ToString(),
            };
            Add("student-free", "student", "Gratuit", true, 0, 0, Student(false, 200_000, false, false, false, false, 1, 20));
            Add("student-standard", "student", "Standard", false, 1, 2500, Student(true, 800_000, true, true, true, false, 3, -1));
            Add("student-premium", "student", "Premium", false, 2, 5000, Student(true, 2_000_000, true, true, true, true, 10, -1));
            Add("student-ultime", "student", "Ultime", false, 3, 7500, Student(true, 3_000_000, true, true, true, true, -1, -1));

            Dictionary<string, string> Parent(int max, bool cat, int credit, bool alerts, int tokens, int bonus, int shares) => new()
            {
                ["children.max"] = max.ToString(),
                ["children.catalog"] = cat ? "true" : "false",
                ["parent.monthly_credit_xaf"] = credit.ToString(),
                ["parent.realtime_alerts"] = alerts ? "true" : "false",
                ["ai.reference_tokens"] = tokens.ToString(),
                ["ai.parent_bonus_per_child"] = bonus.ToString(),
                ["ai.parent_bonus_shares"] = shares.ToString(),
            };
            Add("parent-free", "parent", "Gratuit", true, 0, 0, Parent(1, false, 0, false, 200_000, 0, 0));
            Add("parent-basique", "parent", "Basique", false, 1, 4000, Parent(1, true, 0, false, 400_000, 400_000, 1));
            Add("parent-famille", "parent", "Famille", false, 2, 8000, Parent(3, true, 1000, true, 600_000, 400_000, 3));
            Add("parent-famille-plus", "parent", "Famille+", false, 3, 12000, Parent(5, true, 2000, true, 1_000_000, 500_000, 5));

            Dictionary<string, string> Teacher(int share, bool cat, int classes, int perClass, int courses, int tokens, bool quiz, bool corr, bool builder) => new()
            {
                ["teacher.revenue_share"] = share.ToString(),
                ["catalog.access"] = cat ? "true" : "false",
                ["teacher.max_classes"] = classes.ToString(),
                ["teacher.max_students_per_class"] = perClass.ToString(),
                ["teacher.max_published_courses"] = courses.ToString(),
                ["ai.reference_tokens"] = tokens.ToString(),
                ["ai.feature.quiz_generation"] = quiz ? "true" : "false",
                ["ai.feature.correction_assist"] = corr ? "true" : "false",
                ["ai.feature.course_builder"] = builder ? "true" : "false",
            };
            Add("teacher-free", "teacher", "Gratuit", true, 0, 0, Teacher(70, false, 1, 30, 1, 200_000, false, false, false));
            Add("teacher-fondateur", "teacher", "Fondateur", false, 1, 5000, Teacher(70, true, 3, 50, 3, 1_000_000, true, true, false));
            Add("teacher-pro", "teacher", "Pro", false, 2, 10000, Teacher(75, true, 10, 100, 10, 2_400_000, true, true, true));
            Add("teacher-expert", "teacher", "Expert", false, 3, 15000, Teacher(80, true, -1, -1, -1, 5_000_000, true, true, true));
            return result;
        }

        public static Subscription Subscribe(ApplicationDbContext db, int userId, Family f, string audience, DateTime? end = null,
            string status = SubscriptionStatuses.Active, DateTime? grace = null, string period = "monthly")
        {
            var s = new Subscription
            {
                UserId = userId, PricingPlanId = f.PlanId, PlanVersionId = f.VersionId, Audience = audience,
                BillingPeriod = period, Status = status, IsActive = true,
                StartDate = DateTime.UtcNow.AddDays(-5), EndDate = end ?? DateTime.UtcNow.AddDays(25), GraceUntil = grace,
            };
            db.Subscriptions.Add(s);
            db.SaveChanges();
            return s;
        }
    }
}
