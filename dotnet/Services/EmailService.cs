using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SendGrid;
using SendGrid.Helpers.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

/// <summary>
/// Email service using SendGrid
/// </summary>
public interface IEmailService
{
    Task<bool> SendEmailVerificationAsync(string email, string firstName, string verificationCode);
    Task<bool> SendPasswordResetAsync(string email, string firstName, string resetToken);
    Task<bool> SendPasswordChangedAsync(string email, string firstName);
    Task<bool> SendNewDeviceLoginAsync(string email, string firstName, string deviceName, string ipAddress);
    Task<bool> SendTwoFactorCodeAsync(string email, string firstName, string code);
    Task<bool> SendEmailChangeVerificationAsync(string email, string firstName, string verificationCode);
    Task<bool> SendGenericEmailAsync(string to, string subject, string htmlContent);
}

public class EmailService : IEmailService
{
    private readonly SendGridClient _client;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _logger = logger;

        var apiKey = configuration["SendGrid:ApiKey"] 
            ?? throw new InvalidOperationException("SendGrid:ApiKey not configured");

        _fromEmail = configuration["SendGrid:FromEmail"] ?? "noreply@gogivam.com";
        _fromName = configuration["SendGrid:FromName"] ?? "Winplus Support";

        _client = new SendGridClient(apiKey);

        _logger.LogInformation("EmailService initialized with from email: {FromEmail}", _fromEmail);
    }

    /// <summary>
    /// Send email verification code
    /// </summary>
    public async Task<bool> SendEmailVerificationAsync(string email, string firstName, string verificationCode)
    {
        try
        {
            var subject = "Verify your email on WinPlus";
            var htmlContent = $@"
                <html>
                    <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                        <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                            <h1 style='color: #2196F3;'>Welcome to WinPlus!</h1>
                            <p>Hi {firstName},</p>
                            <p>Thank you for registering with WinPlus. To complete your registration, please verify your email using the code below:</p>
                            
                            <div style='background-color: #f5f5f5; padding: 20px; text-align: center; border-radius: 5px; margin: 20px 0;'>
                                <p style='font-size: 24px; font-weight: bold; letter-spacing: 5px; color: #2196F3;'>{verificationCode}</p>
                                <p style='color: #666; margin-top: 10px;'>This code expires in 24 hours</p>
                            </div>
                            
                            <p>If you didn't register for this account, please ignore this email.</p>
                            <hr style='border: none; border-top: 1px solid #ddd; margin: 20px 0;' />
                            <p style='color: #999; font-size: 12px;'>
                                © 2024 WinPlus. All rights reserved.<br />
                                <a href='https://winplus.com' style='color: #2196F3; text-decoration: none;'>Visit our website</a>
                            </p>
                        </div>
                    </body>
                </html>";

            return await SendGenericEmailAsync(email, subject, htmlContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email verification to {Email}", email);
            return false;
        }
    }

    /// <summary>
    /// Send password reset email
    /// </summary>
    public async Task<bool> SendPasswordResetAsync(string email, string firstName, string resetToken)
    {
        try
        {
            var resetUrl = $"https://winplus.com/reset-password?token={resetToken}";
            var subject = "Reset your WinPlus password";
            var htmlContent = $@"
                <html>
                    <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                        <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                            <h1 style='color: #2196F3;'>Password Reset Request</h1>
                            <p>Hi {firstName},</p>
                            <p>We received a request to reset your WinPlus password. Click the button below to proceed:</p>
                            
                            <div style='text-align: center; margin: 30px 0;'>
                                <a href='{resetUrl}' style='display: inline-block; padding: 12px 30px; background-color: #2196F3; color: white; text-decoration: none; border-radius: 5px; font-weight: bold;'>Reset Password</a>
                            </div>
                            
                            <p style='color: #666;'>Or copy this link: <a href='{resetUrl}' style='color: #2196F3;'>{resetUrl}</a></p>
                            
                            <p style='color: #e74c3c; margin: 20px 0;'><strong>This link expires in 1 hour.</strong></p>
                            
                            <p>If you didn't request a password reset, please ignore this email.</p>
                            <hr style='border: none; border-top: 1px solid #ddd; margin: 20px 0;' />
                            <p style='color: #999; font-size: 12px;'>
                                © 2024 WinPlus. All rights reserved.
                            </p>
                        </div>
                    </body>
                </html>";

            return await SendGenericEmailAsync(email, subject, htmlContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending password reset email to {Email}", email);
            return false;
        }
    }

    /// <summary>
    /// Send password changed confirmation
    /// </summary>
    public async Task<bool> SendPasswordChangedAsync(string email, string firstName)
    {
        try
        {
            var subject = "Your WinPlus password has been changed";
            var htmlContent = $@"
                <html>
                    <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                        <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                            <h1 style='color: #2196F3;'>Password Changed Successfully</h1>
                            <p>Hi {firstName},</p>
                            <p>Your WinPlus account password was changed successfully.</p>
                            
                            <p style='color: #666; margin: 20px 0;'>If you didn't make this change or it wasn't authorized, please contact our support team immediately.</p>
                            
                            <div style='background-color: #f5f5f5; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                <p style='margin: 0;'><strong>Need help?</strong></p>
                                <p style='margin: 5px 0;'><a href='https://winplus.com/support' style='color: #2196F3; text-decoration: none;'>Contact our support team</a></p>
                            </div>
                            
                            <hr style='border: none; border-top: 1px solid #ddd; margin: 20px 0;' />
                            <p style='color: #999; font-size: 12px;'>
                                © 2024 WinPlus. All rights reserved.
                            </p>
                        </div>
                    </body>
                </html>";

            return await SendGenericEmailAsync(email, subject, htmlContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending password changed email to {Email}", email);
            return false;
        }
    }

    /// <summary>
    /// Send new device login notification
    /// </summary>
    public async Task<bool> SendNewDeviceLoginAsync(string email, string firstName, string deviceName, string ipAddress)
    {
        try
        {
            var subject = "New login from a new device on WinPlus";
            var htmlContent = $@"
                <html>
                    <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                        <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                            <h1 style='color: #2196F3;'>New Device Login Detected</h1>
                            <p>Hi {firstName},</p>
                            <p>We detected a login attempt on a new device. Details below:</p>
                            
                            <div style='background-color: #f5f5f5; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                                <p style='margin: 5px 0;'><strong>Device:</strong> {deviceName}</p>
                                <p style='margin: 5px 0;'><strong>IP Address:</strong> {ipAddress}</p>
                                <p style='margin: 5px 0;'><strong>Time:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>
                            </div>
                            
                            <p style='color: #666; margin: 20px 0;'>If this was you, you can dismiss this message. If you don't recognize this device, please change your password immediately.</p>
                            
                            <hr style='border: none; border-top: 1px solid #ddd; margin: 20px 0;' />
                            <p style='color: #999; font-size: 12px;'>
                                © 2024 WinPlus. All rights reserved.
                            </p>
                        </div>
                    </body>
                </html>";

            return await SendGenericEmailAsync(email, subject, htmlContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending new device login email to {Email}", email);
            return false;
        }
    }

    /// <summary>
    /// Send two-factor authentication code
    /// </summary>
    public async Task<bool> SendTwoFactorCodeAsync(string email, string firstName, string code)
    {
        try
        {
            var subject = "Your WinPlus two-factor authentication code";
            var htmlContent = $@"
                <html>
                    <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                        <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                            <h1 style='color: #2196F3;'>Authentication Code</h1>
                            <p>Hi {firstName},</p>
                            <p>Your two-factor authentication code is:</p>
                            
                            <div style='background-color: #f5f5f5; padding: 20px; text-align: center; border-radius: 5px; margin: 20px 0;'>
                                <p style='font-size: 24px; font-weight: bold; letter-spacing: 5px; color: #2196F3;'>{code}</p>
                                <p style='color: #666; margin-top: 10px;'>This code expires in 5 minutes</p>
                            </div>
                            
                            <p style='color: #999; font-size: 12px; margin-top: 20px;'>
                                If you didn't request this code, please ignore this email.
                            </p>
                        </div>
                    </body>
                </html>";

            return await SendGenericEmailAsync(email, subject, htmlContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending 2FA code email to {Email}", email);
            return false;
        }
    }

    /// <summary>
    /// Send email change verification
    /// </summary>
    public async Task<bool> SendEmailChangeVerificationAsync(string email, string firstName, string verificationCode)
    {
        try
        {
            var subject = "Verify your new email address on WinPlus";
            var htmlContent = $@"
                <html>
                    <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                        <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                            <h1 style='color: #2196F3;'>Email Change Verification</h1>
                            <p>Hi {firstName},</p>
                            <p>You requested to change your email address. Please verify your new email using the code below:</p>
                            
                            <div style='background-color: #f5f5f5; padding: 20px; text-align: center; border-radius: 5px; margin: 20px 0;'>
                                <p style='font-size: 24px; font-weight: bold; letter-spacing: 5px; color: #2196F3;'>{verificationCode}</p>
                                <p style='color: #666; margin-top: 10px;'>This code expires in 24 hours</p>
                            </div>
                            
                            <p>If you didn't request this change, please ignore this email.</p>
                            <hr style='border: none; border-top: 1px solid #ddd; margin: 20px 0;' />
                            <p style='color: #999; font-size: 12px;'>
                                © 2024 WinPlus. All rights reserved.
                            </p>
                        </div>
                    </body>
                </html>";

            return await SendGenericEmailAsync(email, subject, htmlContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email change verification to {Email}", email);
            return false;
        }
    }

    /// <summary>
    /// Send generic email
    /// </summary>
    public async Task<bool> SendGenericEmailAsync(string to, string subject, string htmlContent)
    {
        try
        {
            var from = new EmailAddress(_fromEmail, _fromName);
            var toEmail = new EmailAddress(to);
            var msg = new SendGridMessage()
            {
                From = from,
                Subject = subject,
                HtmlContent = htmlContent
            };

            msg.AddTo(toEmail);
            msg.SetClickTracking(false, false);

            var response = await _client.SendEmailAsync(msg);

            if (response.StatusCode == System.Net.HttpStatusCode.Accepted || 
                response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                _logger.LogInformation("Email sent successfully to {Email}", to);
                return true;
            }
            else
            {
                _logger.LogError("Failed to send email to {Email}. Status: {Status}", to, response.StatusCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email to {Email}", to);
            return false;
        }
    }
}
