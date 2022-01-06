﻿/*
 * Acidity V3 Backend - Auth V1 Controller
 * Copyright (c) 2022 EmeraldSys, all rights reserved.
*/

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MongoDB.Bson;
using MongoDB.Driver;

namespace AcidityV3Backend.Controllers
{
    [Route("v1/auth")]
    [ApiController]
    public class AuthV1Controller : ControllerBase
    {
        private MongoClient client;

        public AuthV1Controller()
        {
            MongoClientSettings settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("MONGODB_AUTH_STR"));
            client = new MongoClient(settings);
        }

        [HttpGet]
        public IActionResult Index()
        {
            return Ok(new { Status = "OK" });
        }

        [HttpGet("whitelist")]
        public IActionResult WhitelistAuth([FromQuery]string key, [FromQuery]string hash, [FromQuery]string type)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(type)) return BadRequest(new { Status = "AUTH_BAD", Message = "All or some required parameters are null or empty" });

            IMongoDatabase database = client.GetDatabase("aciditydb");
            IMongoCollection<BsonDocument> collection = database.GetCollection<BsonDocument>("users");

            BsonDocument result = collection.Find(new BsonDocument { { "key", key } }).FirstOrDefault();

            if (result == null) return StatusCode(403, new { Status = "AUTH_FORBIDDEN", Message = "Key is invalid" });

            if (!result.Contains("username") || !result["username"].IsString || !(result.Contains("synFingerprint") || result.Contains("swFingerprint"))) return BadRequest(new { Status = "AUTH_BAD", Message = "User object is invalid" });

            using (SHA512 cipher = SHA512.Create())
            {
                string nonce1 = Environment.GetEnvironmentVariable("NONCE1");
                string nonce2 = Environment.GetEnvironmentVariable("NONCE2");
                DateTime hashDate = DateTime.UtcNow;

                if (type.ToLower() == "syn")
                {
                    string info = "ACIDITYV3_" + nonce1 + key + hash + nonce2 + result["synFingerprint"].AsString + hashDate.Month + hashDate.Day + hashDate.Year + hashDate.Hour;
                    byte[] hashBytes = cipher.ComputeHash(Encoding.UTF8.GetBytes(info));
                    cipher.Dispose();

                    StringBuilder sb = new StringBuilder(128);
                    foreach (byte b in hashBytes)
                    {
                        sb.Append(b.ToString("X2"));
                    }

                    string authHash = sb.ToString().ToLower();
                    return Ok(new { Status = "AUTH_OK", User = result["username"].AsString, Hash = authHash });
                }
                
                if (type.ToLower() == "sw")
                {
                    string info = "ACIDITYV3_" + nonce1 + key + hash + nonce2 + result["swFingerprint"].AsString + hashDate.Month + hashDate.Day + hashDate.Year + hashDate.Hour;
                    byte[] hashBytes = cipher.ComputeHash(Encoding.UTF8.GetBytes(info));
                    cipher.Dispose();

                    StringBuilder sb = new StringBuilder(128);
                    foreach (byte b in hashBytes)
                    {
                        sb.Append(b.ToString("X2"));
                    }

                    string authHash = sb.ToString().ToLower();
                    return Ok(new { Status = "AUTH_OK", User = result["username"].AsString, Hash = authHash });
                }
                
                return BadRequest(new { Status = "AUTH_BAD", Message = "Type not allowed" });
            }
        }

        [HttpPatch("whitelist")]
        public IActionResult WhitelistUpdate([FromQuery]string key, [FromQuery]string type)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(type) || !(Request.Headers.ContainsKey("Syn-Fingerprint") || Request.Headers.ContainsKey("Sw-Fingerprint"))) return BadRequest(new { Status = "AUTH_BAD", Message = "All or some required parameters are null or empty" });

            bool hasSynFingerprint = Request.Headers.TryGetValue("Syn-Fingerprint", out Microsoft.Extensions.Primitives.StringValues synFingerprint);
            bool hasSwFingerprint = Request.Headers.TryGetValue("Sw-Fingerprint", out Microsoft.Extensions.Primitives.StringValues swFingerprint);

            IMongoDatabase database = client.GetDatabase("aciditydb");
            IMongoCollection<BsonDocument> collection = database.GetCollection<BsonDocument>("users");

            if (type.ToLower() == "syn" && hasSynFingerprint)
            {
                UpdateDefinition<BsonDocument> update = Builders<BsonDocument>.Update.Set("synFingerprint", synFingerprint.First());
                collection.FindOneAndUpdate(new BsonDocument { { "key", key } }, update);

                return NoContent();
            }
            
            if (type.ToLower() == "sw" && hasSwFingerprint)
            {
                UpdateDefinition<BsonDocument> update = Builders<BsonDocument>.Update.Set("swFingerprint", swFingerprint.First());
                collection.FindOneAndUpdate(new BsonDocument { { "key", key } }, update);

                return NoContent();
            }
            
            return BadRequest(new { Status = "AUTH_BAD", Message = "Fingerprint and type mismatch" });
        }

        [HttpGet("whitelist/script")]
        public IActionResult ScriptGet([FromQuery]string key, [FromQuery]bool isPre)
        {
            if (string.IsNullOrEmpty(key)) return BadRequest(new { Status = "AUTH_BAD", Message = "Key is null or empty" });

            IMongoDatabase database = client.GetDatabase("aciditydb");
            
            IMongoCollection<BsonDocument> userCollection = database.GetCollection<BsonDocument>("users");
            IMongoCollection<BsonDocument> versionsCollection = database.GetCollection<BsonDocument>("versions");

            BsonDocument result = userCollection.Find(new BsonDocument { { "key", key } }).FirstOrDefault();

            if (result == null) return StatusCode(403, new { Status = "AUTH_FORBIDDEN", Message = "Key is invalid" });

            BsonDocument latest = versionsCollection.Find(new BsonDocument { { "latestStable", true } }).FirstOrDefault();

            if (latest != null && latest.Contains("version") && latest["version"].IsString)
            {
                try
                {
                    //byte[] scriptBytes = System.IO.File.ReadAllBytes(Program.CURRENT_DIR + $"script/{latest["version"].AsString}.lua");
                    //return File(scriptBytes, "text/x-lua; charset=UTF-8", $"{latest["version"].AsString}-latest.lua");
                    string script = System.IO.File.ReadAllText(Program.CURRENT_DIR + $"script/{latest["version"].AsString}.lua");
                    return Content(script, "text/x-lua; charset=UTF-8", Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { Status = "SCRIPT_READ_ERR", Message = ex.Message });
                }
            }

            return NotFound(new { Status = "SCRIPT_NOT_FOUND" });
        }

        [HttpGet("whitelist/script/{version}")]
        public IActionResult ScriptArchiveGet(string version, [FromQuery]string key)
        {
            if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(key)) return BadRequest(new { Status = "AUTH_BAD", Message = "Version and/or key is null or empty" });

            IMongoDatabase database = client.GetDatabase("aciditydb");

            IMongoCollection<BsonDocument> userCollection = database.GetCollection<BsonDocument>("users");
            IMongoCollection<BsonDocument> versionsCollection = database.GetCollection<BsonDocument>("versions");

            BsonDocument result = userCollection.Find(new BsonDocument { { "key", key } }).FirstOrDefault();

            if (result == null) return StatusCode(403, new { Status = "AUTH_FORBIDDEN", Message = "Key is invalid" });

            BsonDocument requested = versionsCollection.Find(new BsonDocument { { "version", version } }).FirstOrDefault();

            if (requested != null)
            {
                try
                {
                    //byte[] scriptBytes = System.IO.File.ReadAllBytes(Program.CURRENT_DIR + $"script/{version}.lua");
                    //return File(scriptBytes, "text/x-lua; charset=UTF-8", $"{version}.lua");
                    string script = System.IO.File.ReadAllText(Program.CURRENT_DIR + $"script/{version}.lua");
                    return Content(script, "text/x-lua; charset=UTF-8", Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { Status = "SCRIPT_READ_ERR", Message = ex.Message });
                }
            }

            return NotFound(new { Status = "SCRIPT_NOT_FOUND" });
        }

        [HttpPatch("whitelist/script/{version}")]
        public async Task<IActionResult> ScriptPublish(string version, [FromQuery]string key, [FromQuery]bool isPre, IFormCollection data)
        {
            if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(key)) return BadRequest(new { Status = "AUTH_BAD", Message = "Version and/or key is null or empty" });

            IFormFileCollection formFiles = data.Files;
            if (formFiles.Count != 1) return BadRequest(new { Status = "REQ_BODY_BAD" });

            IMongoDatabase database = client.GetDatabase("aciditydb");

            IMongoCollection<BsonDocument> userCollection = database.GetCollection<BsonDocument>("users");
            IMongoCollection<BsonDocument> versionsCollection = database.GetCollection<BsonDocument>("versions");

            BsonDocument result = userCollection.Find(new BsonDocument { { "key", key } }).FirstOrDefault();

            if (result == null) return StatusCode(403, new { Status = "AUTH_FORBIDDEN", Message = "Key is invalid" });

            if (!result.Contains("admin") || !result["admin"].IsBoolean || !result["admin"].AsBoolean)
                return StatusCode(403, new { Status = "AUTH_FORBIDDEN", Message = "Missing access" });

            BsonDocument existing = versionsCollection.Find(new BsonDocument { { "version", version } }).FirstOrDefault();

            if (existing == null)
            {
                if (isPre)
                {
                    FilterDefinition<BsonDocument> filterLatestPre = Builders<BsonDocument>.Filter.Eq("latestPre", true);
                    UpdateDefinition<BsonDocument> removeLatestPre = Builders<BsonDocument>.Update.Set("latestPre", false);

                    versionsCollection.FindOneAndUpdate(filterLatestPre, removeLatestPre, new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = false });
                    versionsCollection.InsertOne(new BsonDocument { { "version", version }, { "latestPre", true }, { "latestStable", false } });
                }
                else
                {
                    FilterDefinition<BsonDocument> filterLatestStable = Builders<BsonDocument>.Filter.Eq("latestStable", true);
                    UpdateDefinition<BsonDocument> removeLatestStable = Builders<BsonDocument>.Update.Set("latestStable", false);

                    versionsCollection.FindOneAndUpdate(filterLatestStable, removeLatestStable, new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = false });
                    versionsCollection.InsertOne(new BsonDocument { { "version", version }, { "latestPre", false }, { "latestStable", true } });
                }
            }

            try
            {
                using (FileStream fs = new FileStream(Program.CURRENT_DIR + $"script/{version}.lua", FileMode.Create))
                {
                    await formFiles[0].CopyToAsync(fs);
                    fs.Close();
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Status = "SCRIPT_WRITE_ERR", Message = ex.Message });
            }

            return StatusCode(201, new { Status = "CREATED" });
        }

        [HttpGet("whitelist/scriptHash")]
        public IActionResult ScriptHashGet([FromQuery]string key, [FromQuery]bool isPre)
        {
            if (string.IsNullOrEmpty(key)) return BadRequest(new { Status = "AUTH_BAD", Message = "Key is null or empty" });

            IMongoDatabase database = client.GetDatabase("aciditydb");

            IMongoCollection<BsonDocument> userCollection = database.GetCollection<BsonDocument>("users");
            IMongoCollection<BsonDocument> versionsCollection = database.GetCollection<BsonDocument>("versions");

            BsonDocument result = userCollection.Find(new BsonDocument { { "key", key } }).FirstOrDefault();

            if (result == null) return StatusCode(403, new { Status = "AUTH_FORBIDDEN", Message = "Key is invalid" });

            if (isPre)
            {
                BsonDocument latest = versionsCollection.Find(new BsonDocument { { "latestPre", true } }).FirstOrDefault();

                if (latest != null && latest.Contains("version") && latest["version"].IsString)
                {
                    try
                    {
                        byte[] scriptBytes = System.IO.File.ReadAllBytes(Program.CURRENT_DIR + $"script/{latest["version"].AsString}.lua");
                        using (SHA256 cipher = SHA256.Create())
                        {
                            byte[] hashBytes = cipher.ComputeHash(scriptBytes);
                            cipher.Dispose();

                            StringBuilder sb = new StringBuilder(128);
                            foreach (byte b in hashBytes)
                            {
                                sb.Append(b.ToString("X2"));
                            }

                            string scriptHash = sb.ToString().ToLower();
                            return Ok(new { Status = "OK", Hash = scriptHash });
                        }
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, new { Status = "SCRIPT_READ_ERR", Message = ex.Message });
                    }
                }
            }
            else
            {
                BsonDocument latest = versionsCollection.Find(new BsonDocument { { "latestStable", true } }).FirstOrDefault();

                if (latest != null && latest.Contains("version") && latest["version"].IsString)
                {
                    try
                    {
                        byte[] scriptBytes = System.IO.File.ReadAllBytes(Program.CURRENT_DIR + $"script/{latest["version"].AsString}.lua");
                        using (SHA256 cipher = SHA256.Create())
                        {
                            byte[] hashBytes = cipher.ComputeHash(scriptBytes);
                            cipher.Dispose();

                            StringBuilder sb = new StringBuilder(128);
                            foreach (byte b in hashBytes)
                            {
                                sb.Append(b.ToString("X2"));
                            }

                            string scriptHash = sb.ToString().ToLower();
                            return Ok(new { Status = "OK", Hash = scriptHash });
                        }
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, new { Status = "SCRIPT_READ_ERR", Message = ex.Message });
                    }
                }
            }

            return NotFound(new { Status = "SCRIPT_NOT_FOUND" });
        }

        [HttpGet("whitelist/scriptHash/{version}")]
        public IActionResult ScriptArchiveHashGet(string version, [FromQuery]string key)
        {
            if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(key)) return BadRequest(new { Status = "AUTH_BAD", Message = "Version and/or key is null or empty" });

            IMongoDatabase database = client.GetDatabase("aciditydb");

            IMongoCollection<BsonDocument> userCollection = database.GetCollection<BsonDocument>("users");
            IMongoCollection<BsonDocument> versionsCollection = database.GetCollection<BsonDocument>("versions");

            BsonDocument result = userCollection.Find(new BsonDocument { { "key", key } }).FirstOrDefault();

            if (result == null) return StatusCode(403, new { Status = "AUTH_FORBIDDEN", Message = "Key is invalid" });

            BsonDocument requested = versionsCollection.Find(new BsonDocument { { "version", version } }).FirstOrDefault();

            if (requested != null)
            {
                try
                {
                    byte[] scriptBytes = System.IO.File.ReadAllBytes(Program.CURRENT_DIR + $"script/{version}.lua");
                    using (SHA256 cipher = SHA256.Create())
                    {
                        byte[] hashBytes = cipher.ComputeHash(scriptBytes);
                        cipher.Dispose();

                        StringBuilder sb = new StringBuilder(128);
                        foreach (byte b in hashBytes)
                        {
                            sb.Append(b.ToString("X2"));
                        }

                        string scriptHash = sb.ToString().ToLower();
                        return Ok(new { Status = "OK", Hash = scriptHash });
                    }
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { Status = "SCRIPT_READ_ERR", Message = ex.Message });
                }
            }

            return NotFound(new { Status = "SCRIPT_NOT_FOUND" });
        }
    }
}
