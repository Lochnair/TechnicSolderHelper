﻿using System;
using System.Data.Entity;
using System.Linq;
using System.Web;
using Microsoft.AspNet.SignalR;
using ModpackHelper.Shared.Web.Api;
using ModpackHelper.webmods.db;
using ModpackHelper.webmods.Helpers;
using Newtonsoft.Json;
// ReSharper disable UnusedMember.Global

namespace ModpackHelper.webmods.Hubs
{
    /// <summary>
    /// A hub for managing the single page application running 
    /// for web users
    /// </summary>
    public class ModsHub : Hub<IClient>
    {
        /// <summary>
        /// Get all the mods specified by the request options
        /// </summary>
        /// <param name="requestOptions"></param>
        public void Request(RequestOptions requestOptions)
        {
            // Get the status parameter
            Status s;
            bool validStatus = Enum.TryParse(requestOptions.Status, true, out s);
            if (!validStatus) return;

            using (var db = new ModpackHelperContext())
            {
                // Make sure the client is logged in
                if (!db.Connections.Any(c => c.ConnectionId.Equals(Context.ConnectionId)))
                {
                    Clients.Caller.SetLoggedIn(false);
                    return;
                }


                // MAGIC!
                // Does all the searching
                var mods =
                    db.Mods.Where(
                        m =>
                            m.Status == s &&
                            (m.Name.Contains(requestOptions.Search) || m.Filename.Contains(requestOptions.Search)))
                        .Distinct()
                        .Take(requestOptions.Limit).ToList();
                Clients.Caller.SendMods(mods);
            }

        }

        /// <summary>
        /// Accepts a mod as valid
        /// </summary>
        /// <param name="mod">The mod to accept</param>
        public void Accept(Mod mod)
        {
            mod.Status = Status.Accepted;
            using (var db = new ModpackHelperContext())
            {
                // Make sure the client is logged in
                if (!db.Connections.Any(c => c.ConnectionId.Equals(Context.ConnectionId)))
                {
                    Clients.Caller.SetLoggedIn(false);
                    return;
                }

                // Find all the mods like the accepted one
                var mods = db.Mods.Include(m => m.Authors).Where(m => m.JarMd5.Equals(mod.JarMd5));
                foreach (Mod mod1 in mods)
                {
                    mod1.Status = mod1.Mcversion.Equals(mod.Mcversion) && mod1.Name.Equals(mod.Name) && mod1.Version.Equals(mod.Version) && mod1.Modid.Equals(mod.Modid) && mod1.Authors.Count() == mod.Authors.Count ? Status.Accepted : Status.Denied;
                    // Tell the clients to hide it because it was accepted by someone else
                    Clients.All.RemoveMod(mod1.Id);
                }
                db.SaveChanges();
            }
        }

        /// <summary>
        /// Marks a mod as invalid
        /// </summary>
        /// <param name="mod">The mod to mark invalid</param>
        public void Deny(Mod mod)
        {
            using (var db = new ModpackHelperContext())
            {
                // Make sure the client is logged in
                if (!db.Connections.Any(c => c.ConnectionId.Equals(Context.ConnectionId)))
                {
                    Clients.Caller.SetLoggedIn(false);
                    return;
                }

                // Find all the mods like this one
                var mods = db.Mods.Include(m => m.Authors).Where(m => m.Mcversion.Equals(mod.Mcversion) && m.Name.Equals(mod.Name) && m.Version.Equals(mod.Version) && m.Modid.Equals(mod.Modid) && m.Authors.Count() == mod.Authors.Count);
                foreach (Mod mod1 in mods)
                {
                    mod1.Status = Status.Denied;
                    // Tell the client to remove it because it was denied by someone else
                    Clients.All.RemoveMod(mod1.Id);
                }
                try
                {
                    db.SaveChanges();
                }
                catch (System.Data.Entity.Validation.DbEntityValidationException dbEx)
                {
                    Exception raise = dbEx;
                    foreach (var validationErrors in dbEx.EntityValidationErrors)
                    {
                        foreach (var validationError in validationErrors.ValidationErrors)
                        {
                            string message = string.Format("{0}:{1}",
                                validationErrors.Entry.Entity.ToString(),
                                validationError.ErrorMessage);
                            // raise a new exception nesting
                            // the current instance as InnerException
                            raise = new InvalidOperationException(message, raise);
                        }
                    }
                    throw raise;
                }
            }
        }

        /// <summary>
        /// Login to the service
        /// </summary>
        /// <param name="username"></param>
        /// <param name="password"></param>
        public void LoginUser(string username, string password)
        {
            // Make sure the user actually entered something
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return;
            }
            using (var db = new ModpackHelperContext())
            {
                // Check that the user is not already logged in
                User user = db.Connections.Find(Context.ConnectionId)?.User;
                if (user != null)
                {
                    Clients.Caller.SetLoggedIn(true);
                    return;
                }

                // Find the user to login
                user = db.Users.FirstOrDefault(u => u.Username.Equals(username));
                if (user == null)
                {
                    Clients.Caller.LoginFailed();
                    return;
                }
                // Check if the entered password is correct
                bool success = PasswordHash.ValidatePassword(password, user.Password);
                if (success)
                {
                    // Password was correct
                    user.Connections.Add(new Connection() { ConnectionId = Context.ConnectionId});
                    Clients.Caller.SetLoggedIn(true);

                    db.SaveChanges();
                }
                else
                {
                    // Password was wrong
                    Clients.Caller.LoginFailed();
                }
            }
        }
    }
}