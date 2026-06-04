-- ============================================================================
-- SCHEMA PostgreSQL COMPLET - Plateforme éducative REUSSIR avec AWS Cognito
-- Optimisé pour Agile/Scrum avec extensibilité
-- ============================================================================

-- ======================
-- 1. TABLES UTILISATEURS
-- ======================

-- Utilisateurs (intégration Cognito)
CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    cognito_id VARCHAR(255) UNIQUE NOT NULL,  -- ID Cognito AWS
    email VARCHAR(255) UNIQUE NOT NULL,
    first_name VARCHAR(100),
    last_name VARCHAR(100),
    phone VARCHAR(20),
    profile_image_url VARCHAR(500),
    bio TEXT,
    preferred_language VARCHAR(10) DEFAULT 'fr',
    timezone VARCHAR(50) DEFAULT 'Europe/Paris',
    is_active BOOLEAN DEFAULT true,
    email_verified BOOLEAN DEFAULT false,
    last_login TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Profils utilisateurs (rôles et permissions)
CREATE TABLE user_profiles (
    id SERIAL PRIMARY KEY,
    user_id INTEGER UNIQUE NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role VARCHAR(50) DEFAULT 'student',  -- student, instructor, admin, moderator
    level VARCHAR(50) DEFAULT 'débutant',  -- débutant, intermédiaire, avancé, expert
    learning_goal TEXT,  -- objectif d'apprentissage
    specialization VARCHAR(100),  -- domaine de spécialité
    bio_detailed TEXT,  -- biographie détaillée
    avatar_url VARCHAR(500),
    cover_image_url VARCHAR(500),
    total_hours_learning INTEGER DEFAULT 0,
    total_completed_courses INTEGER DEFAULT 0,
    certificates_count INTEGER DEFAULT 0,
    rating NUMERIC(3, 2) DEFAULT 0,
    rating_count INTEGER DEFAULT 0,
    is_instructor_verified BOOLEAN DEFAULT false,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Préférences utilisateur
CREATE TABLE user_preferences (
    id SERIAL PRIMARY KEY,
    user_id INTEGER UNIQUE NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    notification_email BOOLEAN DEFAULT true,
    notification_push BOOLEAN DEFAULT true,
    notification_sms BOOLEAN DEFAULT false,
    theme_mode VARCHAR(20) DEFAULT 'light',  -- light, dark, auto
    language_ui VARCHAR(10) DEFAULT 'fr',
    auto_play_videos BOOLEAN DEFAULT true,
    subtitle_preference VARCHAR(50) DEFAULT 'auto',  -- auto, fr, en, off
    marketing_emails BOOLEAN DEFAULT false,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ======================
-- 2. TABLES CONTENU/COURS
-- ======================

-- Sujets/Catégories principales
CREATE TABLE categories (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) UNIQUE NOT NULL,
    slug VARCHAR(100) UNIQUE NOT NULL,
    description TEXT,
    icon_url VARCHAR(300),
    parent_id INTEGER REFERENCES categories(id) ON DELETE SET NULL,
    order_index INTEGER,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Cours/Sujets principaux
CREATE TABLE courses (
    id SERIAL PRIMARY KEY,
    title VARCHAR(255) NOT NULL,
    slug VARCHAR(255) UNIQUE,
    description TEXT,
    long_description TEXT,
    category_id INTEGER REFERENCES categories(id) ON DELETE SET NULL,
    instructor_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    thumbnail_url VARCHAR(500),
    banner_url VARCHAR(500),
    price NUMERIC(10, 2) DEFAULT 0,
    currency VARCHAR(3) DEFAULT 'EUR',
    is_published BOOLEAN DEFAULT false,
    publication_date TIMESTAMP,
    level VARCHAR(50) DEFAULT 'intermédiaire',  -- débutant, intermédiaire, avancé
    duration_hours INTEGER,
    enrollment_count INTEGER DEFAULT 0,
    completion_count INTEGER DEFAULT 0,
    average_rating NUMERIC(3, 2) DEFAULT 0,
    total_ratings INTEGER DEFAULT 0,
    language VARCHAR(10) DEFAULT 'fr',
    subtitle_languages VARCHAR(500),  -- json array of languages
    prerequisites TEXT,  -- description des prérequis
    learning_objectives TEXT,  -- json array of objectives
    tags VARCHAR(500),  -- json array
    is_featured BOOLEAN DEFAULT false,
    featured_until TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Sections dans un cours
CREATE TABLE course_sections (
    id SERIAL PRIMARY KEY,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL,
    description TEXT,
    order_index INTEGER NOT NULL,
    duration_minutes INTEGER DEFAULT 0,
    is_locked BOOLEAN DEFAULT false,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Contenus/Leçons
CREATE TABLE course_contents (
    id SERIAL PRIMARY KEY,
    section_id INTEGER NOT NULL REFERENCES course_sections(id) ON DELETE CASCADE,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL,
    description TEXT,
    type VARCHAR(50) DEFAULT 'video',  -- video, document, quiz, exercise, resource
    video_url VARCHAR(500),
    video_duration_seconds INTEGER,
    document_url VARCHAR(500),
    document_type VARCHAR(50),  -- pdf, docx, pptx, etc
    resource_url VARCHAR(500),
    content_body TEXT,  -- pour contenu inline
    order_index INTEGER NOT NULL,
    duration_minutes INTEGER,
    is_locked BOOLEAN DEFAULT false,
    is_preview BOOLEAN DEFAULT false,  -- gratuit en aperçu
    difficulty NUMERIC(3, 2),  -- 0-1 scale
    download_allowed BOOLEAN DEFAULT true,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Ressources additionnelles
CREATE TABLE course_resources (
    id SERIAL PRIMARY KEY,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    content_id INTEGER REFERENCES course_contents(id) ON DELETE CASCADE,
    title VARCHAR(255),
    resource_type VARCHAR(50),  -- pdf, zip, code, template, etc
    file_url VARCHAR(500),
    file_size_mb INTEGER,
    download_count INTEGER DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ======================
-- 3. TABLES INSCRIPTIONS/PROGRESSION
-- ======================

-- Inscriptions aux cours
CREATE TABLE enrollments (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    enrolled_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    completed_at TIMESTAMP,
    progress_percentage NUMERIC(5, 2) DEFAULT 0,
    is_completed BOOLEAN DEFAULT false,
    last_accessed TIMESTAMP,
    time_spent_seconds INTEGER DEFAULT 0,
    certificate_url VARCHAR(500),
    certificate_issued_at TIMESTAMP,
    grade NUMERIC(3, 2),  -- 0-100
    status VARCHAR(50) DEFAULT 'active',  -- active, paused, completed, dropped
    UNIQUE(user_id, course_id),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Progression par contenu
CREATE TABLE content_progress (
    id SERIAL PRIMARY KEY,
    enrollment_id INTEGER NOT NULL REFERENCES enrollments(id) ON DELETE CASCADE,
    content_id INTEGER NOT NULL REFERENCES course_contents(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    completed_at TIMESTAMP,
    time_spent_seconds INTEGER DEFAULT 0,
    watch_percentage NUMERIC(5, 2) DEFAULT 0,  -- pour vidéos
    is_completed BOOLEAN DEFAULT false,
    score NUMERIC(5, 2),  -- si quiz/exercice
    attempts INTEGER DEFAULT 0,
    last_accessed TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(enrollment_id, content_id)
);

-- Historique d'apprentissage (audit trail)
CREATE TABLE learning_history (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    course_id INTEGER REFERENCES courses(id) ON DELETE SET NULL,
    content_id INTEGER REFERENCES course_contents(id) ON DELETE SET NULL,
    activity_type VARCHAR(50),  -- view, watch, complete, quiz, exercise, download, etc
    activity_data JSONB,  -- données flexibles (score, temps, etc)
    time_spent_seconds INTEGER,
    ip_address VARCHAR(45),
    user_agent TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ======================
-- 4. TABLES QUIZ/EXERCICES
-- ======================

-- Quizzes/Tests
CREATE TABLE quizzes (
    id SERIAL PRIMARY KEY,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    content_id INTEGER REFERENCES course_contents(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL,
    description TEXT,
    type VARCHAR(50) DEFAULT 'quiz',  -- quiz, exam, exercise, assignment
    difficulty NUMERIC(3, 2),
    pass_percentage NUMERIC(5, 2) DEFAULT 70,  -- note requise pour valider
    time_limit_minutes INTEGER,
    randomize_questions BOOLEAN DEFAULT true,
    randomize_options BOOLEAN DEFAULT true,
    show_correct_answers BOOLEAN DEFAULT true,
    show_explanation BOOLEAN DEFAULT true,
    allow_retake BOOLEAN DEFAULT true,
    max_attempts INTEGER DEFAULT 3,
    passing_feedback TEXT,
    failing_feedback TEXT,
    order_index INTEGER,
    is_graded BOOLEAN DEFAULT true,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Questions de quiz
CREATE TABLE quiz_questions (
    id SERIAL PRIMARY KEY,
    quiz_id INTEGER NOT NULL REFERENCES quizzes(id) ON DELETE CASCADE,
    question_text TEXT NOT NULL,
    question_type VARCHAR(50) DEFAULT 'multiple-choice',  -- multiple-choice, true-false, short-answer, matching, essay
    points NUMERIC(5, 2) DEFAULT 1,
    order_index INTEGER,
    explanation TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Options de réponse
CREATE TABLE quiz_question_options (
    id SERIAL PRIMARY KEY,
    question_id INTEGER NOT NULL REFERENCES quiz_questions(id) ON DELETE CASCADE,
    option_text TEXT NOT NULL,
    is_correct BOOLEAN DEFAULT false,
    order_index INTEGER,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Réponses aux quiz
CREATE TABLE quiz_responses (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    quiz_id INTEGER NOT NULL REFERENCES quizzes(id) ON DELETE CASCADE,
    enrollment_id INTEGER REFERENCES enrollments(id) ON DELETE CASCADE,
    score NUMERIC(5, 2),
    percentage NUMERIC(5, 2),
    is_passed BOOLEAN,
    time_spent_seconds INTEGER,
    started_at TIMESTAMP,
    completed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    responses_data JSONB,  -- réponses détaillées
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ======================
-- 5. TABLES E-COMMERCE
-- ======================

-- Panier
CREATE TABLE cart_items (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    price NUMERIC(10, 2),
    added_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, course_id)
);

-- Commandes
CREATE TABLE orders (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    order_number VARCHAR(50) UNIQUE NOT NULL,
    total_amount NUMERIC(10, 2),
    discount_amount NUMERIC(10, 2) DEFAULT 0,
    tax_amount NUMERIC(10, 2) DEFAULT 0,
    final_amount NUMERIC(10, 2),
    currency VARCHAR(3) DEFAULT 'EUR',
    status VARCHAR(50) DEFAULT 'pending',  -- pending, completed, failed, cancelled, refunded
    payment_method VARCHAR(50),  -- credit_card, paypal, stripe, etc
    payment_provider VARCHAR(50),  -- stripe, paypal, etc
    transaction_id VARCHAR(255),
    invoice_url VARCHAR(500),
    notes TEXT,
    order_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    completed_date TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Articles de commande
CREATE TABLE order_items (
    id SERIAL PRIMARY KEY,
    order_id INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    course_id INTEGER NOT NULL REFERENCES courses(id),
    title VARCHAR(255),
    price_at_purchase NUMERIC(10, 2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Coupons/Promotions
CREATE TABLE coupons (
    id SERIAL PRIMARY KEY,
    code VARCHAR(50) UNIQUE NOT NULL,
    description TEXT,
    discount_type VARCHAR(20) DEFAULT 'percentage',  -- percentage, fixed
    discount_value NUMERIC(10, 2),
    min_purchase NUMERIC(10, 2),
    max_uses INTEGER,
    current_uses INTEGER DEFAULT 0,
    applicable_courses VARCHAR(500),  -- json array of course_ids, null = all
    valid_from TIMESTAMP,
    valid_until TIMESTAMP,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Favoris/Wishlist
CREATE TABLE favorites (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    added_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, course_id)
);

-- Refunds
CREATE TABLE refunds (
    id SERIAL PRIMARY KEY,
    order_id INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    reason VARCHAR(255),
    refund_amount NUMERIC(10, 2),
    status VARCHAR(50) DEFAULT 'pending',  -- pending, approved, rejected, completed
    requested_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    processed_at TIMESTAMP,
    notes TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ======================
-- 6. TABLES SOCIAL/ENGAGEMENT
-- ======================

-- Avis et notes
CREATE TABLE course_reviews (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    enrollment_id INTEGER REFERENCES enrollments(id) ON DELETE CASCADE,
    rating NUMERIC(3, 2) NOT NULL,  -- 1-5
    title VARCHAR(255),
    comment TEXT,
    is_verified_purchase BOOLEAN DEFAULT false,
    helpful_count INTEGER DEFAULT 0,
    unhelpful_count INTEGER DEFAULT 0,
    is_approved BOOLEAN DEFAULT true,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, course_id)
);

-- Commentaires sur contenus
CREATE TABLE content_comments (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    content_id INTEGER NOT NULL REFERENCES course_contents(id) ON DELETE CASCADE,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    parent_comment_id INTEGER REFERENCES content_comments(id) ON DELETE CASCADE,
    comment_text TEXT NOT NULL,
    is_approved BOOLEAN DEFAULT true,
    is_pinned BOOLEAN DEFAULT false,
    helpful_count INTEGER DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Discussions/Forums
CREATE TABLE discussion_threads (
    id SERIAL PRIMARY KEY,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL,
    description TEXT,
    category VARCHAR(50),  -- questions, announcements, resources, general
    view_count INTEGER DEFAULT 0,
    reply_count INTEGER DEFAULT 0,
    is_pinned BOOLEAN DEFAULT false,
    is_closed BOOLEAN DEFAULT false,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Réponses aux discussions
CREATE TABLE discussion_replies (
    id SERIAL PRIMARY KEY,
    thread_id INTEGER NOT NULL REFERENCES discussion_threads(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    parent_reply_id INTEGER REFERENCES discussion_replies(id) ON DELETE CASCADE,
    reply_text TEXT NOT NULL,
    is_instructor_reply BOOLEAN DEFAULT false,
    is_solution BOOLEAN DEFAULT false,
    helpful_count INTEGER DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Notifications
CREATE TABLE notifications (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    title VARCHAR(255),
    message TEXT,
    notification_type VARCHAR(50),  -- course_update, new_message, review, achievement, etc
    related_entity_type VARCHAR(50),  -- course, comment, review, etc
    related_entity_id INTEGER,
    action_url VARCHAR(500),
    is_read BOOLEAN DEFAULT false,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    read_at TIMESTAMP
);

-- ======================
-- 7. TABLES CERTIFICATS
-- ======================

-- Certificats
CREATE TABLE certificates (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    enrollment_id INTEGER NOT NULL REFERENCES enrollments(id) ON DELETE CASCADE,
    certificate_number VARCHAR(100) UNIQUE NOT NULL,
    certificate_url VARCHAR(500),
    issued_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP,
    is_revoked BOOLEAN DEFAULT false,
    revoke_reason TEXT,
    revoked_at TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Badges/Achievements
CREATE TABLE badges (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) UNIQUE NOT NULL,
    description TEXT,
    icon_url VARCHAR(300),
    criteria_type VARCHAR(50),  -- courses_completed, hours_learned, rating_received, etc
    criteria_value INTEGER,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE user_badges (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    badge_id INTEGER NOT NULL REFERENCES badges(id) ON DELETE CASCADE,
    earned_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, badge_id)
);

-- ======================
-- 8. TABLES ANALYTIQUES
-- ======================

-- Événements analytiques
CREATE TABLE analytics_events (
    id SERIAL PRIMARY KEY,
    user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    event_type VARCHAR(50),  -- page_view, click, signup, enroll, purchase, etc
    event_name VARCHAR(255),
    event_category VARCHAR(100),
    related_entity_type VARCHAR(50),  -- course, content, quiz, etc
    related_entity_id INTEGER,
    event_data JSONB,  -- données dynamiques
    ip_address VARCHAR(45),
    user_agent TEXT,
    session_id VARCHAR(255),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Statistiques quotidiennes (aggregation)
CREATE TABLE daily_statistics (
    id SERIAL PRIMARY KEY,
    stat_date DATE UNIQUE NOT NULL,
    total_users INTEGER DEFAULT 0,
    active_users INTEGER DEFAULT 0,
    new_enrollments INTEGER DEFAULT 0,
    completed_courses INTEGER DEFAULT 0,
    total_revenue NUMERIC(12, 2) DEFAULT 0,
    total_watch_hours INTEGER DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Rapports de cohort
CREATE TABLE cohort_analytics (
    id SERIAL PRIMARY KEY,
    cohort_date DATE,  -- date d'inscription
    cohort_size INTEGER,
    week_1_retention_percentage NUMERIC(5, 2),
    week_2_retention_percentage NUMERIC(5, 2),
    week_4_retention_percentage NUMERIC(5, 2),
    average_rating NUMERIC(3, 2),
    completion_rate NUMERIC(5, 2),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ======================
-- 9. TABLES MODÉRATION
-- ======================

-- Rapports d'abus
CREATE TABLE abuse_reports (
    id SERIAL PRIMARY KEY,
    reported_by_user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    reported_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    reported_content_id INTEGER,
    reported_content_type VARCHAR(50),  -- comment, review, profile, etc
    reason VARCHAR(100),  -- inappropriate, spam, plagiarism, etc
    description TEXT,
    status VARCHAR(50) DEFAULT 'pending',  -- pending, investigating, resolved, dismissed
    action_taken VARCHAR(100),  -- warning, suspension, deletion, etc
    notes TEXT,
    resolved_at TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ======================
-- 10. TABLES APPRENTISSAGE PERSONNALISÉ
-- ======================

-- Recommandations générées
CREATE TABLE recommendations (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    recommendation_score NUMERIC(3, 2),  -- 0-1
    reason VARCHAR(255),  -- 'Similar to completed courses', 'Popular in your category', etc
    recommendation_engine VARCHAR(50),  -- 'collaborative_filtering', 'content_based', 'trending'
    generated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    clicked_at TIMESTAMP,
    enrolled_at TIMESTAMP
);

-- Progression prédictive
CREATE TABLE progress_predictions (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    course_id INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    predicted_completion_date DATE,
    completion_probability NUMERIC(3, 2),  -- 0-1
    risk_factor VARCHAR(50),  -- 'low', 'medium', 'high' (drop-out risk)
    suggested_actions JSONB,
    generated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ======================
-- 11. TABLES GESTION DE PROJET (Agile/Scrum)
-- ======================

-- Sprints
CREATE TABLE sprints (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100),
    start_date DATE NOT NULL,
    end_date DATE NOT NULL,
    goal TEXT,
    status VARCHAR(50) DEFAULT 'planning',  -- planning, active, completed, cancelled
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Tâches/Features
CREATE TABLE features (
    id SERIAL PRIMARY KEY,
    sprint_id INTEGER REFERENCES sprints(id) ON DELETE SET NULL,
    title VARCHAR(255) NOT NULL,
    description TEXT,
    type VARCHAR(50),  -- feature, bug, improvement, task
    priority VARCHAR(50) DEFAULT 'medium',  -- low, medium, high, critical
    status VARCHAR(50) DEFAULT 'todo',  -- todo, in_progress, in_review, done
    story_points INTEGER,
    assigned_to_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- ======================
-- 12. CRÉER LES INDEX
-- ======================

-- Utilisateurs
CREATE INDEX idx_users_cognito_id ON users(cognito_id);
CREATE INDEX idx_users_email ON users(email);
CREATE INDEX idx_users_active ON users(is_active);
CREATE INDEX idx_user_profiles_user_id ON user_profiles(user_id);
CREATE INDEX idx_user_profiles_role ON user_profiles(role);

-- Cours
CREATE INDEX idx_courses_category ON courses(category_id);
CREATE INDEX idx_courses_instructor ON courses(instructor_id);
CREATE INDEX idx_courses_published ON courses(is_published);
CREATE INDEX idx_courses_featured ON courses(is_featured);
CREATE INDEX idx_courses_slug ON courses(slug);
CREATE INDEX idx_course_sections_course ON course_sections(course_id);
CREATE INDEX idx_course_contents_section ON course_contents(section_id);
CREATE INDEX idx_course_contents_course ON course_contents(course_id);

-- Inscriptions
CREATE INDEX idx_enrollments_user ON enrollments(user_id);
CREATE INDEX idx_enrollments_course ON enrollments(course_id);
CREATE INDEX idx_enrollments_status ON enrollments(status);
CREATE INDEX idx_enrollments_created ON enrollments(created_at);
CREATE INDEX idx_content_progress_enrollment ON content_progress(enrollment_id);
CREATE INDEX idx_content_progress_user ON content_progress(user_id);
CREATE INDEX idx_learning_history_user ON learning_history(user_id);
CREATE INDEX idx_learning_history_course ON learning_history(course_id);
CREATE INDEX idx_learning_history_created ON learning_history(created_at);

-- Quiz
CREATE INDEX idx_quizzes_course ON quizzes(course_id);
CREATE INDEX idx_quiz_responses_user ON quiz_responses(user_id);
CREATE INDEX idx_quiz_responses_quiz ON quiz_responses(quiz_id);

-- E-commerce
CREATE INDEX idx_cart_items_user ON cart_items(user_id);
CREATE INDEX idx_orders_user ON orders(user_id);
CREATE INDEX idx_orders_status ON orders(status);
CREATE INDEX idx_orders_created ON orders(order_date);
CREATE INDEX idx_favorites_user ON favorites(user_id);
CREATE INDEX idx_coupons_code ON coupons(code);

-- Social
CREATE INDEX idx_reviews_course ON course_reviews(course_id);
CREATE INDEX idx_reviews_user ON course_reviews(user_id);
CREATE INDEX idx_comments_content ON content_comments(content_id);
CREATE INDEX idx_comments_user ON content_comments(user_id);
CREATE INDEX idx_threads_course ON discussion_threads(course_id);
CREATE INDEX idx_threads_user ON discussion_threads(user_id);
CREATE INDEX idx_replies_thread ON discussion_replies(thread_id);
CREATE INDEX idx_notifications_user ON notifications(user_id);
CREATE INDEX idx_notifications_read ON notifications(is_read);

-- Analytiques
CREATE INDEX idx_analytics_events_user ON analytics_events(user_id);
CREATE INDEX idx_analytics_events_type ON analytics_events(event_type);
CREATE INDEX idx_analytics_events_created ON analytics_events(created_at);

-- Certificats
CREATE INDEX idx_certificates_user ON certificates(user_id);
CREATE INDEX idx_certificates_course ON certificates(course_id);

-- Modération
CREATE INDEX idx_abuse_reports_status ON abuse_reports(status);
CREATE INDEX idx_abuse_reports_created ON abuse_reports(created_at);

-- Recommandations
CREATE INDEX idx_recommendations_user ON recommendations(user_id);
CREATE INDEX idx_recommendations_course ON recommendations(course_id);

-- Agile
CREATE INDEX idx_sprints_dates ON sprints(start_date, end_date);
CREATE INDEX idx_features_sprint ON features(sprint_id);
CREATE INDEX idx_features_status ON features(status);

-- ======================
-- 13. CONSTRAINTS SUPPLÉMENTAIRES
-- ======================

-- Vérifier que le prix est positif
ALTER TABLE courses ADD CONSTRAINT courses_price_positive CHECK (price >= 0);
ALTER TABLE orders ADD CONSTRAINT orders_amount_positive CHECK (final_amount >= 0);
ALTER TABLE quiz_questions ADD CONSTRAINT questions_points_positive CHECK (points > 0);

-- Vérifier les pourcentages
ALTER TABLE quizzes ADD CONSTRAINT quiz_pass_percentage CHECK (pass_percentage BETWEEN 0 AND 100);
ALTER TABLE content_progress ADD CONSTRAINT watch_percentage CHECK (watch_percentage BETWEEN 0 AND 100);
ALTER TABLE daily_statistics ADD CONSTRAINT completion_rate_check CHECK (completion_rate BETWEEN 0 AND 100);

-- Vérifier les notes
ALTER TABLE course_reviews ADD CONSTRAINT review_rating_range CHECK (rating BETWEEN 1 AND 5);
ALTER TABLE courses ADD CONSTRAINT avg_rating_range CHECK (average_rating BETWEEN 0 AND 5);

-- ======================
-- 14. VIEWS UTILES
-- ======================

-- Vue : Statistiques des utilisateurs
CREATE VIEW user_statistics AS
SELECT 
    u.id,
    u.email,
    u.first_name,
    up.role,
    COUNT(DISTINCT e.id) as courses_enrolled,
    COUNT(DISTINCT CASE WHEN e.is_completed THEN e.id END) as courses_completed,
    SUM(DISTINCT e.time_spent_seconds) as total_time_seconds,
    AVG(cr.rating) as average_rating,
    COUNT(DISTINCT c.id) as certificates_earned,
    MAX(u.last_login) as last_login
FROM users u
LEFT JOIN user_profiles up ON u.id = up.user_id
LEFT JOIN enrollments e ON u.id = e.user_id
LEFT JOIN course_reviews cr ON u.id = cr.user_id
LEFT JOIN certificates c ON u.id = c.user_id
GROUP BY u.id, u.email, u.first_name, up.role;

-- Vue : Statistiques des cours
CREATE VIEW course_statistics AS
SELECT 
    c.id,
    c.title,
    COUNT(DISTINCT e.id) as total_enrollments,
    COUNT(DISTINCT CASE WHEN e.is_completed THEN e.id END) as total_completions,
    ROUND(100.0 * COUNT(DISTINCT CASE WHEN e.is_completed THEN e.id END) / NULLIF(COUNT(DISTINCT e.id), 0), 2) as completion_rate,
    AVG(cr.rating) as average_rating,
    COUNT(DISTINCT cr.id) as total_reviews,
    SUM(o.final_amount) as total_revenue
FROM courses c
LEFT JOIN enrollments e ON c.id = e.course_id
LEFT JOIN course_reviews cr ON c.id = cr.course_id
LEFT JOIN order_items oi ON c.id = oi.course_id
LEFT JOIN orders o ON oi.order_id = o.id AND o.status = 'completed'
GROUP BY c.id, c.title;

-- Vue : Engagement des utilisateurs
CREATE VIEW user_engagement AS
SELECT 
    u.id,
    u.email,
    COUNT(DISTINCT e.id) as active_courses,
    COUNT(DISTINCT ae.id) as total_interactions,
    MAX(ae.created_at) as last_activity,
    SUM(cp.time_spent_seconds) as total_learning_time_seconds
FROM users u
LEFT JOIN enrollments e ON u.id = e.user_id AND e.status = 'active'
LEFT JOIN analytics_events ae ON u.id = ae.user_id
LEFT JOIN content_progress cp ON u.id = cp.user_id
GROUP BY u.id, u.email;

-- ======================
-- 15. PARTITIONING (Optionnel - pour très gros volumes)
-- ======================

-- Partitionner par date pour les événements analytiques (après 1M+ de lignes)
-- ALTER TABLE analytics_events PARTITION BY RANGE (DATE_TRUNC('month', created_at));

-- ======================
-- 16. INSERTS DE DONNÉES INITIALES
-- ======================

-- Catégories de base
INSERT INTO categories (name, slug, description, order_index) VALUES
('Programmation', 'programmation', 'Développement logiciel et programmation', 1),
('Mathématiques', 'mathematiques', 'Cours de mathématiques avancées', 2),
('Intelligence Artificielle', 'ia', 'IA, Machine Learning et Deep Learning', 3),
('Langues', 'langues', 'Apprentissage des langues étrangères', 4),
('Culture Générale', 'culture', 'Culture générale et humanités', 5);

-- Badges standards
INSERT INTO badges (name, description, icon_url, criteria_type, criteria_value) VALUES
('Débutant', 'Premier cours complété', '/badges/beginner.png', 'courses_completed', 1),
('Apprenti', '5 cours complétés', '/badges/learner.png', 'courses_completed', 5),
('Maître', '20 cours complétés', '/badges/master.png', 'courses_completed', 20),
('Expert', '50 heures d''apprentissage', '/badges/expert.png', 'hours_learned', 50),
('Participatif', '10 commentaires constructifs', '/badges/social.png', 'community_engagement', 10);

COMMIT;
