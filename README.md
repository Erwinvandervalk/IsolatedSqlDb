[![Build-and-Release](https://github.com/Erwinvandervalk/IsolatedSqlDb/actions/workflows/release.yml/badge.svg)](https://github.com/Erwinvandervalk/IsolatedSqlDb/actions/workflows/release.yml)

# IsolatedSqlDb
Class library to help running isolated unit tests against sql server

When running [unit tests against a real sql database](https://erwin.vandervalk.pro/testing-without-mocks-databases/), you'll 
have the need to isolate the individual tests from each other. 

>> Ideally, i'd try to find a different way to do this (ie: avoid sql altogether, use sqlite etc.)
>> but if you're applying tests to an existing system, you often don't have a choice. 

This library helps you to create isolated sql databases. I've found that the fastest way to create an isolated
database is to create a database with schema, detach it and use that as the basis for all your unit tests. 

Right now, it ONLY works with SQlLocalDb on windows. In the future, I'll also try to get it to work against docker on linux. 

## Overview

This package works in two steps:

**Step 1: Prepare.** This step allows you to create a database, including all required schema's. The database is then detached and the mdb files kept. These files can then be used as the basis for your tests. 

Things to know:
* You can control the name of the sql local db instance to use. 
* You can control where the files go. 
* You sometimes need to do some cleanup (see below)

>> You typically do this either in a command line tool (preffered) or as an assembly initialize.

**Step 2: Copy and Attach**. This step takes a copy of the mdb files and attaches them. You now have an empty disposable database for your usage. 

>> You do this typically per test. 

## Usage

``` c#

/* STEP 1: PREPARE */

// Create a database manager. 
var isolatedDatabaseSettings = new IsolatedDatabaseSettings("the_name_of_your_system");
var mgr = new IsolatedDatabaseManager(isolatedDatabaseSettings, _loggerFactory.CreateLogger<IsolatedDatabaseManager>());

// Create the database mdb files. 
await mgr.Prepare(CreateDatabaseSchema, CancellationToken.None);

private async Task CreateDatabaseSchema(IsolatedDatabase isolatedDatabase, CancellationToken cancellationToken)
{
    // Todo: create your database schema. IE: run migrations, use EF to create database,
    // run manual sql commands. etc..
}

// The mdb file is called: "the_name_of_your_system.mdb". By default, it's stored in your temp files. 

/* STEP 2: use database*/

// This command creates and attaches an isolated database. 
using var db = await mgr.CreateIsolatedDatabase(CancellationToken.None);

// Disposing it will detach / delete your database


```

## Manual cleanup

If your test doesn't invoke dispose, you'll end up with a lot of detached databases. 

I typically do the following:

1. Delete the sql localdb instance 

``` powershell
    # list the instancies
    sqllocaldb i

    # stop selected instance
    sqllocaldb p "your instance name"

    # delete
    sqllocaldb d "your instance name"
```

Then delete the mdb files from your local file system. 

The instance is started automatically again by the testframework. 